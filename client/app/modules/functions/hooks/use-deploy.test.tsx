import { beforeEach, describe, expect, it, vi } from "vitest";
import { act, renderHook, waitFor } from "@testing-library/react";
import { makeHookWrapper } from "@/test-utils/test-providers/render";

const deployFunction = vi.fn();
const getBuild = vi.fn();
vi.mock("../services/function.service", () => ({
  functionService: {
    deployFunction: (...args: unknown[]) => deployFunction(...args),
    getBuild: (...args: unknown[]) => getBuild(...args),
  },
}));

import { splitFailedBuild, useDeploy } from "./use-deploy";

const version = { id: "v1", number: 3, imageDigest: "d", runCount: 0, createdDate: "", createdBy: "" };

const setup = () => {
  const onDeployed = vi.fn();
  const onFailed = vi.fn();
  const hook = renderHook(() => useDeploy("fn_1", { onDeployed, onFailed }), { wrapper: makeHookWrapper() });
  return { hook, onDeployed, onFailed };
};

describe("useDeploy (F-4: deploy does not hold the request for the build)", () => {
  beforeEach(() => {
    deployFunction.mockReset();
    getBuild.mockReset();
  });

  it("a deploy answered with the version is done at once", async () => {
    deployFunction.mockResolvedValue(version);
    const { hook, onDeployed } = setup();

    await act(() => hook.result.current.deploy());

    expect(onDeployed).toHaveBeenCalledWith(version);
    expect(hook.result.current.buildId).toBeUndefined();
    expect(getBuild).not.toHaveBeenCalled();
  });

  it("a 202 watches the build, then deploys exactly that build", async () => {
    deployFunction.mockResolvedValueOnce({ buildId: "b1", status: "Building" }).mockResolvedValueOnce(version);
    getBuild.mockResolvedValue({ id: "b1", status: "Succeeded", createdDate: "" });
    const { hook, onDeployed } = setup();

    await act(() => hook.result.current.deploy());
    expect(hook.result.current.isBuilding).toBe(true);

    await waitFor(() => expect(onDeployed).toHaveBeenCalledWith(version));
    expect(deployFunction).toHaveBeenLastCalledWith({ functionId: "fn_1", buildId: "b1" });
    expect(deployFunction).toHaveBeenCalledTimes(2);
    await waitFor(() => expect(hook.result.current.isBuilding).toBe(false));
  });

  it("a failed build is reported once and its id kept for the log", async () => {
    deployFunction.mockResolvedValue({ buildId: "b2", status: "Queued" });
    getBuild.mockResolvedValue({ id: "b2", status: "Failed", errorMessage: "npm error 404", createdDate: "" });
    const { hook, onFailed } = setup();

    await act(() => hook.result.current.deploy());

    await waitFor(() => expect(onFailed).toHaveBeenCalledTimes(1));
    expect(onFailed).toHaveBeenCalledWith({ errors: "The build failed: npm error 404" });
    expect(hook.result.current.buildId).toBe("b2");
    expect(hook.result.current.isBuilding).toBe(false);
    expect(deployFunction).toHaveBeenCalledTimes(1);
  });

  it("code saved again during the build: the refusal is shown and nothing more is watched", async () => {
    const refusal = { errors: "the code changed after this build started; deploy again" };
    deployFunction.mockResolvedValueOnce({ buildId: "b3", status: "Building" }).mockRejectedValueOnce(refusal);
    getBuild.mockResolvedValue({ id: "b3", status: "Succeeded", createdDate: "" });
    const { hook, onFailed, onDeployed } = setup();

    await act(() => hook.result.current.deploy());

    await waitFor(() => expect(onFailed).toHaveBeenCalledWith(refusal));
    expect(onDeployed).not.toHaveBeenCalled();
    await waitFor(() => expect(hook.result.current.buildId).toBeUndefined());
  });

  it("a deploy request that fails outright is reported", async () => {
    deployFunction.mockRejectedValue({ errors: "the build failed: boom" });
    const { hook, onFailed } = setup();

    await act(() => hook.result.current.deploy());

    expect(onFailed).toHaveBeenCalledWith({ errors: "the build failed: boom" });
  });

  it("a deploy refused for a failed build shows that build's log and reports once (PKG-14)", async () => {
    deployFunction.mockRejectedValue({
      isSuccess: false,
      errors: { invalid_request: "the build failed: npm error code E404", buildId: "b9" },
    });
    getBuild.mockResolvedValue({ id: "b9", status: "Failed", errorMessage: "npm error code E404", createdDate: "" });
    const { hook, onFailed } = setup();

    await act(() => hook.result.current.deploy());

    expect(hook.result.current.buildId).toBe("b9");
    expect(hook.result.current.isBuilding).toBe(false);
    expect(onFailed).toHaveBeenCalledWith({
      isSuccess: false,
      errors: { invalid_request: "the build failed: npm error code E404" },
    });
    await waitFor(() => expect(getBuild).toHaveBeenCalled());
    expect(onFailed).toHaveBeenCalledTimes(1);
  });

  it("a refusal without a build id is passed on unchanged", async () => {
    const refusal = { isSuccess: false, errors: { invalid_request: "the code changed" } };
    deployFunction.mockRejectedValue(refusal);
    const { hook, onFailed } = setup();

    await act(() => hook.result.current.deploy());

    expect(onFailed).toHaveBeenCalledWith(refusal);
    expect(hook.result.current.buildId).toBeUndefined();
  });
});

describe("splitFailedBuild", () => {
  it.each([null, undefined, "boom", { errors: "text" }, { errors: ["a"] }, { errors: { buildId: 7 } }, { errors: { buildId: "" } }])(
    "leaves %j alone",
    (error) => {
      expect(splitFailedBuild(error)).toEqual({ error });
    },
  );
});
