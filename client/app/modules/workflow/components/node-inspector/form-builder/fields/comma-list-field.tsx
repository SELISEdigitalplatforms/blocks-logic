"use client";

import { useState } from "react";
import { Input } from "@/components/ui-kits/input/input";
import { FieldProps } from "../form-field.types";
import { listToText, textToList } from "@blocks-workflow/utils/split-out";

/**
 * A comma-separated text box that saves a list of strings: "email, name" is saved as ["email", "name"].
 * The text is kept as typed, so commas and spaces are not removed mid-typing.
 */
export const CommaListField = ({ field, value, onChange, readOnly }: FieldProps<string[]>) => {
  const [text, setText] = useState(() => listToText(value));

  return (
    <Input
      id={field.id}
      value={text}
      placeholder={field.placeholder}
      disabled={readOnly || !!field.disabled}
      readOnly={readOnly}
      onChange={(e) => {
        setText(e.target.value);
        onChange(textToList(e.target.value));
      }}
    />
  );
};
