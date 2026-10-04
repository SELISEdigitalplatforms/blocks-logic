import { request as playwrightRequest } from "@playwright/test"
import { test, expect } from "../../support/test-base"
import { e2eBaseUrl } from "../../support/env"
import fs from "fs"
import path from "path"

/**
 * API-level coverage for Native Web Push (#273). Uses the authenticated Logic
 * storageState from suite setup. Real browser PushManager delivery is out of
 * reach on headless CI; these tests assert the HTTP contracts for H1–H6 / C1–C6.
 */

const endpointA = "https://fcm.googleapis.com/fcm/send/e2e-device-a"
const endpointB = "https://fcm.googleapis.com/fcm/send/e2e-device-b"

function success(body: Record<string, unknown>): boolean {
  const v = body.isSuccess ?? body.IsSuccess
  return v === true
}

function publicKey(body: Record<string, unknown>): string {
  return String(body.publicKey ?? body.PublicKey ?? "")
}

async function resolveBlocksKey(): Promise<string> {
  const res = await fetch(`${e2eBaseUrl()}/runtime-config.js`)
  if (!res.ok) {
    throw new Error(`runtime-config.js HTTP ${res.status}`)
  }
  const body = await res.text()
  const match = body.match(/BLOCKS_X_BLOCKS_KEY:\s*"([^"]+)"/)
  if (!match?.[1]) {
    throw new Error("BLOCKS_X_BLOCKS_KEY missing from runtime-config.js")
  }
  return match[1]
}

async function api(storageStatePath: string | undefined) {
  const blocksKey = await resolveBlocksKey()
  return playwrightRequest.newContext({
    baseURL: e2eBaseUrl(),
    ignoreHTTPSErrors: true,
    extraHTTPHeaders: { "x-blocks-key": blocksKey },
    ...(storageStatePath && fs.existsSync(storageStatePath)
      ? { storageState: storageStatePath }
      : {}),
  })
}

test.describe("Web Push API", () => {
  const sessionPath = path.resolve(__dirname, "../../fixtures/logic-session.json")

  test("GetWebPushPublicKey is idempotent (H2/H3)", async () => {
    const ctx = await api(sessionPath)
    const r1 = await ctx.get("/api/Notifier/GetWebPushPublicKey")
    expect(r1.status()).toBe(200)
    const key1 = publicKey(await r1.json())
    expect(key1).toBeTruthy()

    const r2 = await ctx.get("/api/Notifier/GetWebPushPublicKey")
    expect(r2.status()).toBe(200)
    expect(publicKey(await r2.json())).toBe(key1)
    await ctx.dispose()
  })

  test("register / unregister / validation / notify contracts (H1/H5/C1/C2/C4)", async () => {
    const ctx = await api(sessionPath)

    const bad = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: { endpoint: endpointA, keys: { p256dh: "", auth: "" } },
    })
    expect(bad.status()).toBe(200)
    expect(success(await bad.json())).toBeFalsy()

    const regA = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: {
        endpoint: endpointA,
        keys: { p256dh: "BN4e2e-device-a", auth: "k8Je2e-a" },
      },
    })
    expect(regA.status()).toBe(200)
    expect(success(await regA.json())).toBeTruthy()

    const regB = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: {
        endpoint: endpointB,
        keys: { p256dh: "BN4e2e-device-b", auth: "k8Je2e-b" },
      },
    })
    expect(regB.status()).toBe(200)
    expect(success(await regB.json())).toBeTruthy()

    const unknown = await ctx.post("/api/Notifier/Notify", {
      data: {
        configurationName: "does-not-exist-webpush-e2e",
        userIds: ["nobody"],
        denormalizedPayload: '{"title":"x"}',
      },
    })
    expect(unknown.status()).toBe(200)
    expect(success(await unknown.json())).toBeFalsy()

    const unreg = await ctx.post("/api/Notifier/UnregisterWebPushSubscription", {
      data: { endpoint: endpointA },
    })
    expect(success(await unreg.json())).toBeTruthy()

    const unregAgain = await ctx.post("/api/Notifier/UnregisterWebPushSubscription", {
      data: { endpoint: endpointA },
    })
    expect(success(await unregAgain.json())).toBeTruthy()

    await ctx.post("/api/Notifier/UnregisterWebPushSubscription", {
      data: { endpoint: endpointB },
    })
    await ctx.dispose()
  })

  test("unauthenticated register/rotate return 401 (C5)", async () => {
    const ctx = await api(undefined)
    const reg = await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: {
        endpoint: endpointA,
        keys: { p256dh: "x", auth: "y" },
      },
    })
    expect([401, 403]).toContain(reg.status())

    const rotate = await ctx.post("/api/Notifier/RotateWebPushVapidKeys")
    expect([401, 403]).toContain(rotate.status())
    await ctx.dispose()
  })

  test("RotateWebPushVapidKeys changes public key (H6/C6)", async () => {
    const ctx = await api(sessionPath)
    const beforeRes = await ctx.get("/api/Notifier/GetWebPushPublicKey")
    const beforeKey = publicKey(await beforeRes.json())

    await ctx.post("/api/Notifier/RegisterWebPushSubscription", {
      data: {
        endpoint: endpointA,
        keys: { p256dh: "BN4rotate", auth: "k8Jrotate" },
      },
    })

    const rotate = await ctx.post("/api/Notifier/RotateWebPushVapidKeys")
    expect(rotate.status()).toBe(200)
    expect(success(await rotate.json())).toBeTruthy()

    const afterRes = await ctx.get("/api/Notifier/GetWebPushPublicKey")
    const afterKey = publicKey(await afterRes.json())
    expect(afterKey).toBeTruthy()
    expect(afterKey).not.toBe(beforeKey)
    await ctx.dispose()
  })
})
