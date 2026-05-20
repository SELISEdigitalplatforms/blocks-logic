type Position = { x: number; y: number };

interface NodePosition {
  position: Position;
  measured?: { width?: number; height?: number };
  width?: number;
  height?: number;
}

const DEFAULT_NODE_WIDTH = 260;
const DEFAULT_NODE_HEIGHT = 60;
const SPACING = 40;

export function findFreePosition(nodes: NodePosition[], preferredPosition?: Position): Position {
  const startPos = preferredPosition || { x: 250, y: 100 };

  const hasOverlap = (pos: Position): boolean => {
    return nodes.some((node) => {
      const nodeWidth = node.measured?.width || node.width || DEFAULT_NODE_WIDTH;
      const nodeHeight = node.measured?.height || node.height || DEFAULT_NODE_HEIGHT;
      const dx = Math.abs(pos.x - node.position.x);
      const dy = Math.abs(pos.y - node.position.y);
      return dx < nodeWidth + SPACING && dy < nodeHeight + SPACING;
    });
  };

  if (!hasOverlap(startPos)) {
    return startPos;
  }

  // Try positions in order of preference: right, below, diagonal, left, further right
  const offsets = [
    { x: DEFAULT_NODE_WIDTH + SPACING, y: 0 }, // Right
    { x: 0, y: DEFAULT_NODE_HEIGHT + SPACING }, // Below
    { x: DEFAULT_NODE_WIDTH + SPACING, y: DEFAULT_NODE_HEIGHT + SPACING }, // Diagonal bottom-right
    { x: -(DEFAULT_NODE_WIDTH + SPACING), y: 0 }, // Left
    { x: (DEFAULT_NODE_WIDTH + SPACING) * 2, y: 0 }, // Further right
    { x: 0, y: (DEFAULT_NODE_HEIGHT + SPACING) * 2 }, // Further below
  ];

  for (const offset of offsets) {
    const candidate = {
      x: startPos.x + offset.x,
      y: startPos.y + offset.y,
    };
    if (!hasOverlap(candidate)) {
      return candidate;
    }
  }

  // Final fallback: place it further down vertically
  return {
    x: startPos.x,
    y: startPos.y + (DEFAULT_NODE_HEIGHT + SPACING) * 3,
  };
}
