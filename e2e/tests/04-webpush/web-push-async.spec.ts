import { request as playwrightRequest } from "@playwright/test"
import { test, expect } from "../../support/test-base"
import { e2eBaseUrl } from "../../support/env"
import fs from "fs"
import path from "path"

/**
 * Web Push async delivery (#274). Notify queues one delivery command per device and
 * returns without waiting for the push service. The device below points at an
 * RFC 5737 TEST-NET address that never answers, so a synchronous send would hang
 * until its timeout while a queued send returns at once.
 *
 * Retry timing (5s / 30s), 410 cleanup and bus-down behaviour need a scriptable push
 * endpoint and are covered by unit tests (WebPushDeliveryServiceTests,
 * WebPushNotificationServiceProviderTests, WebPushDeliveryDispatcherTests).
 */

const unreachableEndpoint = "https://192.0.2.10/push/e2e-async-device"
const notifyBudgetMs = 10_000

function success(body: Record<string, unknown>): boolean {
  return (body.isSuccess ?? body.IsSuccess) === true
}

async function resolveBlocksKey(): Promise<string> {
  const res = await fetch(`${e2eBaseUrl()}/runtime-config.js`)
  if (!res.ok) {
    throw new Error(`runtime-config.js HTTP ${res.status}`)
  }
  const match = (await res.text()).match(/BLOCKS_X_BLOCKS_KEY:\s*"([^"]+)"/)
  if (!match?.[1]) {
    throw new Error("BLOCKS_X_BLOCKS_KEY missing from runtime-config.js")
  }
  return match[1]
}

function accessTokenFromStorage(storageStatePath: string): string | undefined {
  const raw = JSON.parse(fs.readFileSync(storageStatePath, "utf8")) as {
    cookies?: Array<{ name: string; value: string }>
  }
  const host = new URL(e2eBaseUrl()).hostname
  return raw.cookies?.find((c) => c.name === host)?.value
}

async function api(storageStatePath: string) {
  const headers: Record<string, string> = { "x-blocks-key": await resolveBlocksKey() }
  const token = fs.existsSync(storageStatePath) ? accessTokenFromStorage(storageStatePath) : undefined
  if (token) {
    headers.Authorization = `Bearer ${token}`
  }
  return playwrightRequest.newContext({
    baseURL: e2eBaseUrl(),
    ignoreHTTPSErrors: true,
    extraHTTPHeaders: headers,
    ...(fs.existsSync(storageStatePath) ? { storageState: storageStatePath } : {}),
  })
}

type Configuration = { name: string; channelToNotify: number; notificationType: number }

async function configurations(ctx: Awaited<ReturnType<typeof api>>): Promise<Configuration[]> {
  const res = await ctx.get("/api/Notification/Gets?PageNumber=0&PageSize=100")
  expect(res.status()).toBe(200)
  const body = (await res.json()) as { configurations?: Configuration[] }
  return body.configurations ?? []
}

function notifyBody(configurationName: string, userIds: string[]) {
  return {
    configurationName,
    userIds,
    denormalizedPayload: '{"title":"e2e async web push"}',
    connectionId: "e2e-webpush-async",
    responseKey: "webpush-async",
    responseValue: "e2e",
  }
}

test.describe("Web Push async delivery", () => {
  const sessionPath = path.resolve(__dirname, "../../fixtures/logic-session.json")

  test("registering a device on an unreachable push service still succeeds (C5)", async () => {
    const ctx = await api(sessionPath)
    const reg = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: { endpoint: unreachableEndpoint, keys: { p256dh: "BN4e2e-async", auth: "k8Je2e-async" } },
    })
    expect(reg.status()).toBe(200)
    expect(success(await reg.json())).toBeTruthy()

    const again = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: { endpoint: unreachableEndpoint, keys: { p256dh: "BN4e2e-async-2", auth: "k8Je2e-async-2" } },
    })
    expect(success(await again.json())).toBeTruthy()

    const unreg = await ctx.post("/api/Notifier/UnregisterWebPushSubscription", {
      data: { endpoint: unreachableEndpoint },
    })
    expect(success(await unreg.json())).toBeTruthy()
    await ctx.dispose()
  })

  test("non-WebPush notify is unchanged and returns promptly", async () => {
    const ctx = await api(sessionPath)
    const signalR = (await configurations(ctx)).find(
      (c) => c.channelToNotify === 0 && c.notificationType === 2,
    )
    expect(signalR, "tenant has a user-specific SignalR configuration").toBeTruthy()

    const started = Date.now()
    const res = await ctx.post("/api/Notifier/Notify", {
      data: notifyBody(signalR!.name, ["e2e-webpush-async-nobody"]),
    })
    const elapsed = Date.now() - started
    expect(res.status()).toBe(200)
    expect(success(await res.json())).toBeTruthy()
    expect(elapsed).toBeLessThan(notifyBudgetMs)
    await ctx.dispose()
  })

  test("notify with zero registered devices succeeds without waiting (H5)", async () => {
    const ctx = await api(sessionPath)
    const webPush = (await configurations(ctx)).find((c) => c.channelToNotify === 2)
    test.skip(!webPush, "no WebPush notification configuration exists on this tenant")

    const started = Date.now()
    const res = await ctx.post("/api/Notifier/Notify", {
      data: notifyBody(webPush!.name, ["e2e-webpush-async-nobody"]),
    })
    expect(res.status()).toBe(200)
    expect(success(await res.json())).toBeTruthy()
    expect(Date.now() - started).toBeLessThan(notifyBudgetMs)
    await ctx.dispose()
  })

  test("notify returns before the push service answers (H1)", async () => {
    const ctx = await api(sessionPath)
    const webPush = (await configurations(ctx)).find((c) => c.channelToNotify === 2)
    test.skip(!webPush, "no WebPush notification configuration exists on this tenant")

    const reg = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: { endpoint: unreachableEndpoint, keys: { p256dh: "BN4e2e-async", auth: "k8Je2e-async" } },
    })
    expect(success(await reg.json())).toBeTruthy()

    try {
      const started = Date.now()
      const res = await ctx.post("/api/Notifier/Notify", {
        data: notifyBody(webPush!.name, ["e2e-webpush-async-self"]),
      })
      expect(res.status()).toBe(200)
      expect(success(await res.json())).toBeTruthy()
      expect(Date.now() - started).toBeLessThan(notifyBudgetMs)
    } finally {
      await ctx.post("/api/Notifier/UnregisterWebPushSubscription", { data: { endpoint: unreachableEndpoint } })
      await ctx.dispose()
    }
  })
})
