import type { APIResponse, Page } from "@playwright/test"
import { test, expect } from "../../support/test-base"
import { openWorkflowList } from "../../support/workflow-helpers"

/**
 * Logic API paths that used to fail with 500 — spec blocks-logic-402 §10 steps 1–6 and 10, through the API.
 *
 * The calls reuse the signed-in console's own credentials: the headers of the workflow list request the
 * console sends while the Workflow page opens. The calls themselves only read; opening the Workflow page
 * may seed the suite's shared workflow when the project has none (openWorkflowList). The mail cases (a
 * template without a subject or body) cannot be set up from here and are covered by unit tests (Q1: B).
 */

const LIST_FIELDS = [
  "id",
  "workflowId",
  "workflowName",
  "status",
  "executionMode",
  "startedAt",
  "finishedAt",
  "errorMessage",
  "attemptNumber",
]

type ExecutionItem = { id: string; startedAt: string } & Record<string, unknown>
type ExecutionsBody = { totalCount: number; data: ExecutionItem[]; errors: unknown }
type WorkflowSummary = { id?: string; itemId?: string }

type Api = {
  origin: string
  /** The tenant key the console sends; kept for the unauthenticated calls so they reach authentication. */
  blocksKey: string | undefined
  get: (path: string) => Promise<APIResponse>
}

/** Opens the Workflow page and returns an API caller with the console's own auth headers, plus the listed workflows. */
async function signedInApi(page: Page): Promise<{ api: Api; workflowIds: string[] }> {
  const nextListResponse = () =>
    page.waitForResponse(
      (response) =>
        response.request().method() === "POST" && /\/api\/Workflow\/GetAll$/.test(new URL(response.url()).pathname),
      { timeout: 60_000 },
    )
  const firstList = nextListResponse()
  await openWorkflowList(page)
  let response = await firstList
  if (((await response.json()) as { data?: unknown[] }).data?.length === 0) {
    // The first list came back before openWorkflowList seeded a workflow: read it once more.
    const reloaded = nextListResponse()
    await page.reload()
    response = await reloaded
  }
  const sent = await response.request().allHeaders()
  const origin = new URL(response.url()).origin
  // The console authenticates with its session cookie, the tenant key and its own Origin/Referer (no bearer
  // header on this host); send the same set, as the browser did.
  const headers: Record<string, string> = Object.fromEntries(
    Object.entries(sent).filter(([name]) => ["cookie", "authorization", "x-blocks-key"].includes(name.toLowerCase())),
  )
  headers.origin = origin
  headers.referer = `${origin}/`
  const body = (await response.json()) as { data?: WorkflowSummary[] }
  const workflowIds = (body.data ?? []).map((w) => w.id ?? w.itemId).filter((id): id is string => !!id)

  return {
    api: {
      origin,
      blocksKey: Object.entries(headers).find(([name]) => name.toLowerCase() === "x-blocks-key")?.[1],
      get: (path) => page.request.get(`${origin}${path}`, { headers }),
    },
    workflowIds,
  }
}

function isNewestFirst(items: ExecutionItem[]): boolean {
  return items.every((item, i) => {
    if (i === 0) return true
    const previous = items[i - 1]
    const a = Date.parse(previous.startedAt)
    const b = Date.parse(item.startedAt)
    return a > b || (a === b && previous.id > item.id)
  })
}

async function executions(api: Api, query: string): Promise<ExecutionsBody> {
  const response = await api.get(`/api/Workflow/GetExecutions?${query}`)
  expect(response.status(), `GetExecutions?${query}`).toBe(200)
  return (await response.json()) as ExecutionsBody
}

test.describe("Logic API — paths that used to fail with 500", () => {
  test("legacy executions list, notification paging and 401 without a token", async ({ page, playwright }, testInfo) => {
    const { api, workflowIds } = await signedInApi(page)
    expect(workflowIds.length, "the Workflow page seeds at least one workflow").toBeGreaterThan(0)

    await test.step("[H1, H3] Executions without PageSize: every execution, the nine list fields, newest first", async () => {
      let checked = 0
      for (const workflowId of workflowIds.slice(0, 10)) {
        const body = await executions(api, `WorkflowId=${encodeURIComponent(workflowId)}`)

        expect(body.errors).toBeNull()
        expect(body.totalCount).toBe(body.data.length)
        for (const item of body.data) {
          expect(LIST_FIELDS, `fields of execution ${item.id}`).toEqual(expect.arrayContaining(Object.keys(item)))
          expect(item.workflowId).toBe(workflowId)
        }
        expect(isNewestFirst(body.data), `order of ${workflowId}`).toBe(true)
        checked += body.data.length
      }
      testInfo.annotations.push({ type: "executions checked", description: String(checked) })
    })

    await test.step("[H4] The paged and cursor paths return the same executions as before, in the same order", async () => {
      for (const workflowId of workflowIds.slice(0, 10)) {
        const id = encodeURIComponent(workflowId)
        const legacy = await executions(api, `WorkflowId=${id}`)
        if (legacy.data.length > 100) continue

        const first = await executions(api, `WorkflowId=${id}&PageSize=2`)
        const older =
          first.data.length === 2
            ? await executions(api, `WorkflowId=${id}&PageSize=2&BeforeId=${encodeURIComponent(first.data[1].id)}`)
            : undefined
        // The shared project can gain an execution between the calls (another suite, a schedule); compare only
        // when the legacy list is the same before and after the paged reads.
        const again = await executions(api, `WorkflowId=${id}`)
        if (again.totalCount !== legacy.totalCount || again.data[0]?.id !== legacy.data[0]?.id) {
          testInfo.annotations.push({ type: "H4 skipped (list changed during the check)", description: workflowId })
          continue
        }

        expect(first.data.map((e) => e.id)).toEqual(legacy.data.slice(0, 2).map((e) => e.id))
        expect(first.totalCount).toBe(legacy.totalCount)
        if (older) {
          expect(older.data.map((e) => e.id)).toEqual(legacy.data.slice(2, 4).map((e) => e.id))
        }
      }
    })

    await test.step("[C13] A workflow without executions is an empty list", async () => {
      const body = await executions(api, `WorkflowId=e2e-no-such-workflow-${Date.now()}`)

      expect(body).toEqual(expect.objectContaining({ totalCount: 0, data: [] }))
      expect(body.errors).toBeNull()
    })

    await test.step("[H5-H8] Valid notification pages are 200 with today's shape", async () => {
      for (const [query, limit] of [
        ["Page=0&PageSize=10", 10],
        ["Page=2&PageSize=10", 10],
        ["PageSize=100", 100],
        ["", 10],
      ] as const) {
        const response = await api.get(`/api/Notifier/GetNotifications${query ? `?${query}` : ""}`)
        expect(response.status(), `GetNotifications?${query}`).toBe(200)
        const body = (await response.json()) as Record<string, unknown>

        expect(Object.keys(body)).toEqual(
          expect.arrayContaining(["notifications", "unReadNotificationsCount", "totalNotificationsCount"]),
        )
        expect(Array.isArray(body.notifications)).toBe(true)
        expect((body.notifications as unknown[]).length).toBeLessThanOrEqual(limit)
      }
    })

    await test.step("[C1-C4] Invalid notification paging is a 400 naming each wrong parameter", async () => {
      const cases: [string, Record<string, string>][] = [
        ["Page=-1&PageSize=10", { Page: "Page must be 0 or greater." }],
        ["PageSize=0", { PageSize: "PageSize must be between 1 and 100." }],
        ["PageSize=101", { PageSize: "PageSize must be between 1 and 100." }],
        ["Page=2147483647&PageSize=10", { Page: "Page is too large for this PageSize." }],
        [
          "Page=-1&PageSize=0",
          { Page: "Page must be 0 or greater.", PageSize: "PageSize must be between 1 and 100." },
        ],
      ]
      for (const [query, errors] of cases) {
        const response = await api.get(`/api/Notifier/GetNotifications?${query}`)

        expect(response.status(), `GetNotifications?${query}`).toBe(400)
        expect(await response.json()).toEqual({ isSuccess: false, errors })
      }
    })

    await test.step("[C14] Without a token the same calls are still 401, before any validation", async () => {
      // No Authorization and no cookies: only the tenant key, so the preview host resolves the tenant and the
      // request reaches authentication.
      const anonymous = await playwright.request.newContext({
        ignoreHTTPSErrors: true,
        extraHTTPHeaders: api.blocksKey ? { "x-blocks-key": api.blocksKey } : {},
      })
      try {
        for (const path of [
          "/api/Notifier/GetNotifications?Page=-1&PageSize=0",
          "/api/Notifier/GetNotifications?Page=0&PageSize=10",
          `/api/Workflow/GetExecutions?WorkflowId=${encodeURIComponent(workflowIds[0])}`,
        ]) {
          expect((await anonymous.get(`${api.origin}${path}`)).status(), path).toBe(401)
        }
        // Without a token these are refused before anything runs; the purpose does not exist and there is no
        // recipient, so even a regression that let them through could not send a mail.
        for (const path of ["/api/Mail/Send", "/api/Mail/SendToAny"]) {
          const response = await anonymous.post(`${api.origin}${path}`, {
            data: { purpose: "e2e-no-such-purpose", language: "en", to: [], isTestMail: true },
          })
          expect(response.status(), path).toBe(401)
        }
      } finally {
        await anonymous.dispose()
      }
    })
  })
})
