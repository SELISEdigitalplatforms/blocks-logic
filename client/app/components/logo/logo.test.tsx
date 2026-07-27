import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { Logo } from "./index";

describe("Logo", () => {
  it("renders a provided src as a single image", () => {
    const { container } = render(<Logo src="/custom.svg" alt="Custom" />);
    const imgs = container.querySelectorAll("img");
    expect(imgs).toHaveLength(1);
    expect(imgs[0].getAttribute("src")).toBe("/custom.svg");
    expect(imgs[0].getAttribute("alt")).toBe("Custom");
  });

  it("renders both light and dark default logos when no src is given", () => {
    const { container } = render(<Logo width={40} height={40} />);
    const imgs = container.querySelectorAll("img");
    expect(imgs).toHaveLength(2);
    expect(imgs[0].getAttribute("src")).toBe("/Logo.svg");
    expect(imgs[1].getAttribute("src")).toBe("/Logo_White.svg");
  });

  it("falls back to the default alt text", () => {
    const { container } = render(<Logo />);
    expect(container.querySelector("img")?.getAttribute("alt")).toBe(
      "SELISE Logo",
    );
  });
});
