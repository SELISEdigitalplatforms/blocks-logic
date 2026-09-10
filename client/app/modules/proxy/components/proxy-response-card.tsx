import { useEffect, useMemo, useRef, useState } from "react";
import { Control, useController, useFormContext } from "react-hook-form";
import { ListChecks, ListFilter, Plus, SquareArrowRight } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { cn } from "@/lib/utils";
import { ProxyFormValues, ProxyMethod, ResponseFieldNode, SampleResult } from "../types";
import {
  countResponseLeaves,
  deriveResponseSchema,
  isResponsePath,
  MAX_PROJECTABLE_BYTES,
  MAX_RESPONSE_PATH_LENGTH,
  MAX_RESPONSE_PATH_SEGMENTS,
  MAX_RESPONSE_PATHS,
  mergeSchemaIntoTree,
  pathsToTree,
  skeletonToPaths,
  treeToPaths,
  treeToSkeleton,
} from "../utils";
import {
  collectIds,
  expandCoverage,
  findNode,
  hasCheckedDescendant,
  mapTree,
  newNode,
  pathTo,
  validateFieldKey,
} from "./response-field-tree.helpers";
import { ResponseFieldTree } from "./response-field-tree";
import { ResponseSkeletonEditor } from "./response-skeleton-editor";

type Props = {
  control: Control<ProxyFormValues>;
  /** Present for parity with the request-body card; the configured `runSample` already carries them. */
  draft?: ProxyFormValues;
  method?: ProxyMethod;
  /** Runs a Test with filtering forced off — see {@link SampleResult}. */
  runSample: () => Promise<SampleResult>;
  /** Re-seeds the tree from the form when it changes (e.g. a saved proxy finishes loading). */
  seedKey?: string;
};

const prettyBytes = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

/**
 * The "Response" card. `responseMode` + `responseInclude` are the single source of truth (form
 * fields); the field tree and its checked-id set are ephemeral component state rebuilt from
 * `responseInclude` on mount / when `seedKey` changes. Every tree mutation re-encodes the checked
 * subtree to a minimal path list and pushes it onto the form.
 */
export const ProxyResponseCard = ({ control, runSample, seedKey }: Props) => {
  const { setError, clearErrors } = useFormContext<ProxyFormValues>();
  const { field: modeField } = useController({ control, name: "responseMode" });
  const { field: includeField } = useController({ control, name: "responseInclude" });

  const [tree, setTree] = useState<ResponseFieldNode[]>([]);
  const [checked, setChecked] = useState<Set<string>>(new Set());
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [wholeResponse, setWholeResponse] = useState(false);
  const [sampleNote, setSampleNote] = useState<string | null>(null);
  const [sampleBusy, setSampleBusy] = useState(false);
  const [skeletonDraft, setSkeletonDraft] = useState<string | null>(null);
  const [skeletonError, setSkeletonError] = useState<string | null>(null);

  // Unnamed rows (and anything below one) are ignored by `treeToPaths`, so a half-added field
  // never reaches the form.
  const encodedPaths = useMemo(
    () => (wholeResponse ? [] : treeToPaths(tree, checked)),
    [tree, checked, wholeResponse],
  );

  // Rebuild the field tree from the form's `responseInclude`: on mount, when the loaded proxy
  // changes (`seedKey`), and, in edit mode, when the parent's async `form.reset` lands the saved
  // paths a tick after this component has mounted. Two guards keep this from fighting the rest of
  // the component:
  //   - `lastPushedRef` holds the exact list we last wrote to the form, so our own push echoing
  //     back through `includeField.value` is recognised and ignored (no reseed / render loop).
  //   - `touchedRef` latches once the user edits the tree; from then on the form value is theirs
  //     and an external `form.reset` (e.g. a background proxy refetch) can never revert it.
  const seededKeyRef = useRef<string | undefined>(undefined);
  const touchedRef = useRef(false);
  const lastPushedRef = useRef<string[]>([]);
  const savedPaths = includeField.value ?? [];
  const savedSig = savedPaths.join("\n");

  const sameList = (a: string[], b: string[]) =>
    a.length === b.length && a.every((value, index) => value === b[index]);

  useEffect(() => {
    const keyChanged = seededKeyRef.current !== seedKey;
    if (keyChanged) {
      seededKeyRef.current = seedKey;
      touchedRef.current = false;
    }
    if (touchedRef.current) return; // user is editing: their selection wins
    if (!keyChanged && sameList(savedPaths, lastPushedRef.current)) return; // our own echo

    const seeded = pathsToTree(savedPaths);
    setTree(seeded.tree);
    setChecked(seeded.checked);
    setWholeResponse(false);
    setSkeletonDraft(null);
    setSkeletonError(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [seedKey, savedSig]);

  // Push the encoded selection onto the form whenever the tree / checks / whole-response flag move.
  useEffect(() => {
    const current = includeField.value ?? [];
    if (!sameList(current, encodedPaths)) {
      lastPushedRef.current = encodedPaths;
      includeField.onChange(encodedPaths);
    }
    if (wholeResponse && modeField.value !== "all") modeField.onChange("all");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [encodedPaths, wholeResponse]);

  // Inline (non-schema) validation that must also block submit. A leaf unnamed row is simply
  // dropped, but a bad character in a key, a duplicate sibling, an unnamed *parent* (its named
  // descendants vanish with it), and the server's path caps all have to stop a save.
  const errorSet = useRef(false);
  useEffect(() => {
    const problems: string[] = [];
    if (modeField.value === "select") {
      const walk = (nodes: ResponseFieldNode[]) => {
        for (const node of nodes) {
          const key = node.key.trim();
          const keyProblem = validateFieldKey(node.key);
          const dupes = nodes.filter((sib) => sib.key.trim() && sib.key.trim() === key);
          if (keyProblem) problems.push(keyProblem);
          else if (key && dupes.length > 1) problems.push(`Duplicate field "${key}".`);
          else if (!key && node.children.length > 0)
            problems.push("A nested field has no name — name it or remove it.");
          walk(node.children);
        }
      };
      walk(tree);

      const tooDeep = encodedPaths.find(
        (path) => path.split(".").length > MAX_RESPONSE_PATH_SEGMENTS,
      );
      if (tooDeep) problems.push(`"${tooDeep}" is nested too deep.`);

      const tooLong = encodedPaths.find((path) => path.length > MAX_RESPONSE_PATH_LENGTH);
      if (tooLong)
        problems.push(
          `A field path is over ${MAX_RESPONSE_PATH_LENGTH} characters — shorten or remove it.`,
        );

      if (encodedPaths.length > MAX_RESPONSE_PATHS)
        problems.push(
          `Too many fields selected (${encodedPaths.length}) — the limit is ${MAX_RESPONSE_PATHS}.`,
        );
    }

    if (problems.length) {
      errorSet.current = true;
      setError("responseInclude", { type: "manual", message: problems[0] });
    } else if (errorSet.current) {
      errorSet.current = false;
      clearErrors("responseInclude");
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tree, checked, modeField.value, encodedPaths]);

  const selectedCount = encodedPaths.length;
  const summary = wholeResponse
    ? "Whole response forwarded"
    : modeField.value === "select" && selectedCount === 0
      ? "Nothing selected — clients receive {}"
      : `${selectedCount} field${selectedCount === 1 ? "" : "s"} selected`;
  const summaryAmber = modeField.value === "select" && !wholeResponse && selectedCount === 0;

  // ---- tree mutations -------------------------------------------------------

  // Runs after every user-driven tree mutation: mark the selection as user-owned (so the seeding
  // effect never reseeds over it) and drop the now-stale manual skeleton draft.
  const resyncSkeleton = () => {
    touchedRef.current = true;
    setSkeletonDraft(null);
    setSkeletonError(null);
  };

  const toggleNode = (node: ResponseFieldNode) => {
    setChecked((current) => {
      const next = new Set(current);
      const trail = pathTo(tree, node.id) ?? [];
      const ancestors = trail.slice(0, -1);
      const inherited = ancestors.some((ancestor) => current.has(ancestor.id));
      const on =
        inherited || current.has(node.id) || hasCheckedDescendant(node, current);

      if (on) {
        // Expand any checked ancestor into its direct children so unchecking this node does not
        // silently keep it selected through the ancestor's subtree coverage.
        for (const ancestor of ancestors) {
          if (next.has(ancestor.id)) {
            next.delete(ancestor.id);
            ancestor.children.forEach((child) => next.add(child.id));
          }
        }
        const subtree = new Set<string>();
        collectIds(node, subtree);
        subtree.forEach((id) => next.delete(id));
      } else {
        // Minimal encoding: a checked node covers its subtree, so drop any descendant checks.
        const descendants = new Set<string>();
        node.children.forEach((child) => collectIds(child, descendants));
        descendants.forEach((id) => next.delete(id));
        next.add(node.id);
      }
      return next;
    });
    resyncSkeleton();
  };

  const renameNode = (id: string, key: string) => {
    setTree((current) =>
      mapTree(current, (node) => (node.id === id ? { ...node, key } : node)),
    );
    resyncSkeleton();
  };

  const removeNode = (id: string) => {
    const target = findNode(tree, id);
    const ids = new Set<string>();
    if (target) collectIds(target, ids);
    setTree((current) => mapTree(current, (node) => (node.id === id ? null : node)));
    setChecked((current) => {
      const next = new Set(current);
      ids.forEach((each) => next.delete(each));
      return next;
    });
    resyncSkeleton();
  };

  const addChild = (parentId: string) => {
    const child = newNode();
    setTree((current) =>
      mapTree(current, (node) =>
        node.id === parentId ? { ...node, children: [...node.children, child] } : node,
      ),
    );
    setChecked((current) => {
      // A subtree-covered parent swallows its children in the encoding — expand that coverage
      // onto the existing children first so the new field lands as its own path.
      const next = new Set(current);
      expandCoverage(tree, next, parentId);
      next.add(child.id);
      return next;
    });
    setCollapsed((current) => {
      const next = new Set(current);
      next.delete(parentId);
      return next;
    });
    resyncSkeleton();
  };

  const addSibling = (siblingId: string) => {
    const node = newNode();
    const insert = (nodes: ResponseFieldNode[]): ResponseFieldNode[] => {
      const index = nodes.findIndex((each) => each.id === siblingId);
      if (index >= 0) {
        const copy = [...nodes];
        copy.splice(index + 1, 0, node);
        return copy;
      }
      return nodes.map((each) => ({ ...each, children: insert(each.children) }));
    };
    setTree((current) => insert(current));
    setChecked((current) => {
      const next = new Set(current);
      // Same as addChild: if the shared parent is subtree-covered, expand it so each sibling
      // (the new one included) encodes as its own path.
      const parent = pathTo(tree, siblingId)?.at(-2);
      if (parent) expandCoverage(tree, next, parent.id);
      next.add(node.id);
      return next;
    });
    resyncSkeleton();
  };

  const addRootField = () => {
    const node = newNode();
    setTree((current) => [...current, node]);
    setChecked((current) => new Set(current).add(node.id));
    resyncSkeleton();
  };

  const toggleCollapsed = (id: string) =>
    setCollapsed((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  // ---- fill from test run ---------------------------------------------------

  const onFill = async () => {
    setSampleBusy(true);
    setSampleNote(null);
    let result: SampleResult;
    try {
      result = await runSample();
    } finally {
      setSampleBusy(false);
    }

    if (!result.ok && result.error) {
      setSampleNote(`Couldn't reach the endpoint — ${result.error}`);
      return;
    }
    if (result.bytes > MAX_PROJECTABLE_BYTES) {
      setSampleNote(
        `Response is ${prettyBytes(result.bytes)}, over the 5 MB filter limit — can't build a tree, and live calls would return 502.`,
      );
      return;
    }
    if (result.status < 200 || result.status >= 300) {
      setSampleNote(
        `Endpoint returned ${result.status}. Filled nothing — add fields by hand or fix the endpoint.`,
      );
      return;
    }

    let parsed: unknown;
    try {
      parsed = JSON.parse(result.body);
    } catch {
      setSampleNote("Response wasn't JSON — nothing to fill.");
      return;
    }

    const { rootKind, tree: schema } = deriveResponseSchema(parsed);
    if (rootKind === "primitive") {
      setWholeResponse(true);
      setSampleNote("This endpoint returns a single value — the whole response is forwarded.");
      return;
    }

    const merged = mergeSchemaIntoTree(tree, checked, schema);
    setTree(merged.tree);
    setChecked(merged.checked);
    setWholeResponse(false);
    resyncSkeleton();
    setSampleNote(`Filled ${countResponseLeaves(schema)} fields from the response.`);
  };

  // ---- skeleton editor ----------------------------------------------------

  const skeletonText =
    skeletonDraft ?? JSON.stringify(treeToSkeleton(tree, checked), null, 2);

  const onSkeletonChange = (value: string) => {
    touchedRef.current = true;
    setSkeletonDraft(value);
    if (!value.trim()) {
      setSkeletonError(null);
      return;
    }
    let parsed: unknown;
    try {
      parsed = JSON.parse(value);
    } catch (error) {
      setSkeletonError(error instanceof Error ? error.message : "Invalid JSON.");
      return;
    }
    const paths = skeletonToPaths(parsed);
    const bad = paths.find((path) => !isResponsePath(path));
    if (bad) {
      setSkeletonError(
        `"${bad}" isn’t a usable field path — keys can’t contain "." "[" or "]", and nesting can’t exceed ${MAX_RESPONSE_PATH_SEGMENTS} levels.`,
      );
      return;
    }
    setSkeletonError(null);
    const seeded = pathsToTree(paths);
    setTree(seeded.tree);
    setChecked(seeded.checked);
    setWholeResponse(false);
  };

  // ---- render -----------------------------------------------------------

  // The "Filter fields" body also stays visible while `wholeResponse` holds — the form's
  // `responseMode` is forced to "all" then (so a save sends everything) but the locked
  // whole-response checkbox is shown inside this section, not the plain passthrough panel.
  const showSelect = modeField.value === "select" || wholeResponse;

  const onTabChange = (value: string) => {
    if (value === "all") {
      setWholeResponse(false);
      modeField.onChange("all");
    } else {
      modeField.onChange("select");
    }
  };

  return (
    <Card className="rounded-xl">
      <CardContent className="space-y-4 p-0">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
          <div className="min-w-0">
            <h3 className="text-sm font-semibold">Response</h3>
            <p className="text-sm text-muted-foreground">
              Choose which fields of the vendor&apos;s JSON reach your client.
            </p>
          </div>
          <Tabs
            value={showSelect ? "select" : "all"}
            onValueChange={onTabChange}
            className="w-full flex-shrink-0 sm:w-auto"
          >
            <TabsList className="grid h-10 w-full grid-cols-2 rounded-lg bg-muted p-1 sm:w-auto">
              <TabsTrigger value="all" className="gap-1.5 rounded-md px-3 text-sm">
                <ListChecks className="h-4 w-4" />
                All fields
              </TabsTrigger>
              <TabsTrigger value="select" className="gap-1.5 rounded-md px-3 text-sm">
                <ListFilter className="h-4 w-4" />
                Filter fields
              </TabsTrigger>
            </TabsList>
          </Tabs>
        </div>

        {!showSelect ? (
          <div className="flex items-start gap-3 rounded-lg border border-dashed bg-muted/20 p-4 text-sm text-muted-foreground">
            <p className="pt-0.5">
              The vendor&apos;s response is relayed to your client unchanged.
            </p>
          </div>
        ) : (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="gap-2"
                disabled={sampleBusy}
                onClick={onFill}
              >
                <SquareArrowRight className="h-3.5 w-3.5" />
                {sampleBusy ? "Running…" : "Fill from test run"}
              </Button>
              <Button
                type="button"
                variant="outline"
                size="sm"
                className="gap-2"
                disabled={wholeResponse}
                onClick={addRootField}
              >
                <Plus className="h-3.5 w-3.5" />
                Add root field
              </Button>
              <span
                className={cn(
                  "ml-auto text-xs",
                  summaryAmber ? "text-amber-600 dark:text-amber-500" : "text-muted-foreground",
                )}
              >
                {summary}
              </span>
            </div>
            {sampleNote ? (
              <p className="text-xs text-muted-foreground">{sampleNote}</p>
            ) : null}

            <div className="grid gap-4 lg:grid-cols-2">
              <div className="flex max-h-[420px] flex-col overflow-y-auto rounded-lg border bg-card p-3">
                <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  Fields
                </p>
                {wholeResponse ? (
                  <div className="space-y-2 text-sm">
                    <label className="flex items-center gap-2 text-muted-foreground">
                      <Checkbox checked disabled />
                      Whole response — nothing to filter
                    </label>
                    {sampleNote ? (
                      <p className="text-xs text-muted-foreground">{sampleNote}</p>
                    ) : null}
                    <button
                      type="button"
                      className="text-xs text-primary underline"
                      onClick={() => setWholeResponse(false)}
                    >
                      Filter fields instead
                    </button>
                  </div>
                ) : tree.length === 0 ? (
                  <p className="py-4 text-center text-xs text-muted-foreground">
                    No fields yet. Add one by hand, or “Fill from test run”.
                  </p>
                ) : (
                  <ResponseFieldTree
                    nodes={tree}
                    checked={checked}
                    collapsed={collapsed}
                    onToggle={toggleNode}
                    onRename={renameNode}
                    onAddChild={addChild}
                    onAddSibling={addSibling}
                    onRemove={removeNode}
                    onCollapse={toggleCollapsed}
                  />
                )}
              </div>

              <ResponseSkeletonEditor
                value={skeletonText}
                error={skeletonError}
                onChange={onSkeletonChange}
              />
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
};
