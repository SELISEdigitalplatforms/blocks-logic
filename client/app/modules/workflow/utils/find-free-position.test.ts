import { describe, it, expect } from "vitest";
import { findFreePosition } from "./find-free-position";

// Constants mirroring the module's internals
const DEFAULT_NODE_WIDTH = 260;
const DEFAULT_NODE_HEIGHT = 60;
const SPACING = 40;

describe("findFreePosition", () => {
  it("should return the default position when there are no nodes", () => {
    const result = findFreePosition([]);
    expect(result).toEqual({ x: 250, y: 100 });
  });

  it("should return the preferred position when it is free", () => {
    const preferredPosition = { x: 800, y: 600 };
    const result = findFreePosition([], preferredPosition);
    expect(result).toEqual(preferredPosition);
  });

  it("should not place a node on top of an existing one at the same position", () => {
    const occupiedNode = { position: { x: 250, y: 100 } };
    const result = findFreePosition([occupiedNode]);
    expect(result).not.toEqual({ x: 250, y: 100 });
  });

  it("should not place a node within the overlap zone of an existing node", () => {
    const existingNode = { position: { x: 250, y: 100 } };
    const result = findFreePosition([existingNode]);

    const dx = Math.abs(result.x - existingNode.position.x);
    const dy = Math.abs(result.y - existingNode.position.y);

    const overlaps = dx < DEFAULT_NODE_WIDTH + SPACING && dy < DEFAULT_NODE_HEIGHT + SPACING;
    expect(overlaps).toBe(false);
  });

  it("should use measured dimensions when available to determine overlap", () => {
    const nodeWithMeasured = {
      position: { x: 250, y: 100 },
      measured: { width: 200, height: 80 },
    };

    const result = findFreePosition([nodeWithMeasured]);
    const dx = Math.abs(result.x - nodeWithMeasured.position.x);
    const dy = Math.abs(result.y - nodeWithMeasured.position.y);
    const overlaps =
      dx < nodeWithMeasured.measured.width + SPACING &&
      dy < nodeWithMeasured.measured.height + SPACING;
    expect(overlaps).toBe(false);
  });

  it("should use explicit width/height properties when measured is absent", () => {
    const nodeWithWidthHeight = {
      position: { x: 250, y: 100 },
      width: 300,
      height: 120,
    };

    const result = findFreePosition([nodeWithWidthHeight]);
    const dx = Math.abs(result.x - nodeWithWidthHeight.position.x);
    const dy = Math.abs(result.y - nodeWithWidthHeight.position.y);
    const overlaps =
      dx < nodeWithWidthHeight.width + SPACING && dy < nodeWithWidthHeight.height + SPACING;
    expect(overlaps).toBe(false);
  });

  it("should still return a position when all offset candidates are occupied", () => {
    // Place nodes at: start, right, below, diagonal, left, further-right, further-below
    const start = { x: 250, y: 100 };
    const candidates = [
      start,
      { x: start.x + DEFAULT_NODE_WIDTH + SPACING, y: start.y }, // right
      { x: start.x, y: start.y + DEFAULT_NODE_HEIGHT + SPACING }, // below
      {
        x: start.x + DEFAULT_NODE_WIDTH + SPACING,
        y: start.y + DEFAULT_NODE_HEIGHT + SPACING,
      }, // diagonal
      { x: start.x - (DEFAULT_NODE_WIDTH + SPACING), y: start.y }, // left
      { x: start.x + (DEFAULT_NODE_WIDTH + SPACING) * 2, y: start.y }, // further right
      { x: start.x, y: start.y + (DEFAULT_NODE_HEIGHT + SPACING) * 2 }, // further below
    ].map((pos) => ({ position: pos }));

    const result = findFreePosition(candidates);
    // Fallback position: start.y + (DEFAULT_NODE_HEIGHT + SPACING) * 3
    expect(result).toEqual({ x: start.x, y: start.y + (DEFAULT_NODE_HEIGHT + SPACING) * 3 });
  });

  it("should return preferred position when it does not collide with any node", () => {
    const farAwayPreferred = { x: 5000, y: 5000 };
    const existingNode = { position: { x: 250, y: 100 } };
    const result = findFreePosition([existingNode], farAwayPreferred);
    expect(result).toEqual(farAwayPreferred);
  });
});
