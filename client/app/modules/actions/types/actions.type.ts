/** One action an editor may pick (`Actions/GetForWorkflow`, `Actions/GetForFunction`). */
export interface IActionOption {
  /** The action name, e.g. `iam.user.created`: what the trigger saves. */
  name: string;
  label: string;
  /** The producing service, e.g. `iam`. */
  service: string;
  /** Field name to type text: the fields of `data`, e.g. `{ userId: "string" }`. */
  payload: Record<string, string>;
}
