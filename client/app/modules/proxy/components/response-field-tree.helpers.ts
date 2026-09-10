import { ResponseFieldNode } from "../types";

let uidSeq = 0;
const uid = () => `rf-new-${Date.now().toString(36)}-${(uidSeq++).toString(36)}`;

export const newNode = (): ResponseFieldNode => ({
  id: uid(),
  key: "",
  isList: false,
  children: [],
});

export const collectIds = (node: ResponseFieldNode, into: Set<string>) => {
  into.add(node.id);
  node.children.forEach((child) => collectIds(child, into));
};

export const mapTree = (
  nodes: ResponseFieldNode[],
  fn: (node: ResponseFieldNode) => ResponseFieldNode | null,
): ResponseFieldNode[] =>
  nodes
    .map((node) => {
      const mapped = fn(node);
      if (!mapped) return null;
      return { ...mapped, children: mapTree(mapped.children, fn) };
    })
    .filter((node): node is ResponseFieldNode => node !== null);

export const findNode = (
  nodes: ResponseFieldNode[],
  id: string,
): ResponseFieldNode | null => {
  for (const node of nodes) {
    if (node.id === id) return node;
    const inChild = findNode(node.children, id);
    if (inChild) return inChild;
  }
  return null;
};

/** Ordered list of nodes from a root down to (and including) the node with `id`, or null. */
export const pathTo = (
  nodes: ResponseFieldNode[],
  id: string,
  trail: ResponseFieldNode[] = [],
): ResponseFieldNode[] | null => {
  for (const node of nodes) {
    const next = [...trail, node];
    if (node.id === id) return next;
    const found = pathTo(node.children, id, next);
    if (found) return found;
  }
  return null;
};

/**
 * Push blanket selection off the node at `id` and every checked ancestor of it down onto their
 * direct children. Adding a field by hand under a subtree-covered node would otherwise be
 * swallowed by the ancestor's path (minimal encoding stops at the first checked node), so the
 * new child never reaches the form. No-op when nothing on the trail is checked.
 */
export const expandCoverage = (
  nodes: ResponseFieldNode[],
  checked: Set<string>,
  id: string,
): void => {
  const trail = pathTo(nodes, id);
  if (!trail) return;
  for (const node of trail) {
    if (checked.has(node.id)) {
      checked.delete(node.id);
      node.children.forEach((child) => checked.add(child.id));
    }
  }
};

export const hasCheckedDescendant = (
  node: ResponseFieldNode,
  checked: Set<string>,
): boolean =>
  node.children.some(
    (child) => checked.has(child.id) || hasCheckedDescendant(child, checked),
  );

/**
 * Whether the checkbox for `node` reads as selected. A checked ancestor covers its whole
 * subtree, so a node inherits its state; a node with only some descendants checked also reads
 * as selected (there is no separate indeterminate state — one click clears the subtree).
 */
export const isNodeSelected = (
  node: ResponseFieldNode,
  checked: Set<string>,
  inherited: boolean,
): boolean =>
  inherited || checked.has(node.id) || hasCheckedDescendant(node, checked);
