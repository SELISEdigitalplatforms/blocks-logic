// Load-test function: ~WORK_MS of CPU per call (at full speed), like a small API handler.
export default async function (input) {
  const until = Date.now() + (input?.workMs ?? 5);
  let x = 0;
  while (Date.now() < until) x += Math.sqrt(x + 1);
  return { ok: true, x: x > 0 };
}
