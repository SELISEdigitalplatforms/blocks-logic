import { describe, expect, it } from "vitest";
import { render } from "@testing-library/react";
import { renderRichText } from "./rich-text";

describe("renderRichText", () => {
  it("wraps the tagged part and keeps the rest as text", () => {
    const { container } = render(
      <p>{renderRichText("Delete <0>demo</0> now.", [(text) => <strong>{text}</strong>])}</p>,
    );
    expect(container.innerHTML).toBe("<p>Delete <strong>demo</strong> now.</p>");
  });

  it("shows only the text of an unknown tag", () => {
    const { container } = render(<p>{renderRichText("Use <3>this</3>.", [])}</p>);
    expect(container.innerHTML).toBe("<p>Use this.</p>");
  });

  it("returns plain text when there is no tag", () => {
    expect(renderRichText("Plain", [])).toEqual(["Plain"]);
  });
});
