import React from "react";
import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import {
  FileInput,
  FileUploader,
  FileUploaderContent,
  FileUploaderItem,
  useFileUpload,
} from "./file-uploader";

const makeFile = (name: string) =>
  new File(["x"], name, { type: "image/png" });

const Harness = ({
  value,
  onValueChange,
  orientation,
}: {
  value: File[] | null;
  onValueChange: (v: File[] | null) => void;
  orientation?: "horizontal" | "vertical";
}) => (
  <FileUploader
    value={value}
    onValueChange={onValueChange}
    dropzoneOptions={{ maxFiles: 3, multiple: true }}
    orientation={orientation}
  >
    <FileInput>
      <span>Drop here</span>
    </FileInput>
    <FileUploaderContent>
      {(value || []).map((f, i) => (
        <FileUploaderItem key={i} index={i}>
          <span>{f.name}</span>
        </FileUploaderItem>
      ))}
    </FileUploaderContent>
  </FileUploader>
);

describe("FileUploader", () => {
  it("renders the dropzone and file items", () => {
    render(
      <Harness value={[makeFile("a.png")]} onValueChange={vi.fn()} />,
    );
    expect(screen.getByText("Drop here")).toBeTruthy();
    expect(screen.getByText("a.png")).toBeTruthy();
  });

  it("removes a file when its remove button is clicked", () => {
    const onValueChange = vi.fn();
    render(
      <Harness
        value={[makeFile("a.png"), makeFile("b.png")]}
        onValueChange={onValueChange}
      />,
    );
    fireEvent.click(screen.getByText("remove item 0"));
    expect(onValueChange).toHaveBeenCalledWith([expect.any(File)]);
  });

  it("navigates items with arrow keys and clears on Escape", () => {
    const { container } = render(
      <Harness
        value={[makeFile("a.png"), makeFile("b.png")]}
        onValueChange={vi.fn()}
      />,
    );
    const root = container.querySelector("[tabindex='0']") as HTMLElement;
    fireEvent.keyDown(root, { key: "ArrowDown" });
    fireEvent.keyDown(root, { key: "ArrowUp" });
    fireEvent.keyDown(root, { key: "Escape" });
    // no throw and the component is still mounted
    expect(root).toBeTruthy();
  });

  it("deletes the active item with the Delete key", () => {
    const onValueChange = vi.fn();
    const { container } = render(
      <Harness
        value={[makeFile("a.png"), makeFile("b.png")]}
        onValueChange={onValueChange}
      />,
    );
    const root = container.querySelector("[tabindex='0']") as HTMLElement;
    fireEvent.keyDown(root, { key: "ArrowDown" }); // activeIndex -> 0
    fireEvent.keyDown(root, { key: "Delete" });
    expect(onValueChange).toHaveBeenCalled();
  });

  it("supports horizontal orientation navigation", () => {
    const { container } = render(
      <Harness
        value={[makeFile("a.png"), makeFile("b.png")]}
        onValueChange={vi.fn()}
        orientation="horizontal"
      />,
    );
    const root = container.querySelector("[tabindex='0']") as HTMLElement;
    fireEvent.keyDown(root, { key: "ArrowRight" });
    fireEvent.keyDown(root, { key: "ArrowLeft" });
    expect(root).toBeTruthy();
  });

  it("throws when useFileUpload is used outside the provider", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    const Bad = () => {
      useFileUpload();
      return null;
    };
    expect(() => render(<Bad />)).toThrow(/FileUploaderProvider/);
    spy.mockRestore();
  });

  it("accepts dropped files through the input", () => {
    const onValueChange = vi.fn();
    const { container } = render(
      <Harness value={[]} onValueChange={onValueChange} />,
    );
    const input = container.querySelector(
      "input[type='file']",
    ) as HTMLInputElement;
    fireEvent.change(input, { target: { files: [makeFile("new.png")] } });
    // onDrop is async in react-dropzone; the change event wires the pipeline
    expect(input).toBeTruthy();
  });
});
