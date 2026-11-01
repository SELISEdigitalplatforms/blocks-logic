import { Fragment, type ReactNode } from "react";

type TagRenderer = (children: string) => ReactNode;

const TAG_PATTERN = /<(\d+)>([^<]*)<\/\1>/g;

/**
 * Shows a translated sentence that holds numbered tags, e.g. `Delete <0>{{name}}</0> now.`
 * Tag `<0>` uses the first renderer, `<1>` the second, and so on; an unknown tag shows its text only.
 * Tags do not nest.
 */
export const renderRichText = (text: string, tags: readonly TagRenderer[]): ReactNode[] => {
  const parts: ReactNode[] = [];
  let last = 0;
  for (const match of text.matchAll(TAG_PATTERN)) {
    const [whole, name, inner] = match;
    if (match.index > last) parts.push(text.slice(last, match.index));
    const render = tags[Number(name)];
    parts.push(<Fragment key={match.index}>{render ? render(inner) : inner}</Fragment>);
    last = match.index + whole.length;
  }
  if (last < text.length) parts.push(text.slice(last));
  return parts;
};
