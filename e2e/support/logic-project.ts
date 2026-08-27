import fs from "fs"
import path from "path"

export type LogicProjectFixture = {
  projectName: string
  itemId: string
  dashboardUrl: string
}

const FIXTURE_PATH = path.resolve(__dirname, "../fixtures/logic-project.json")
export const LOGIC_SESSION_PATH = path.resolve(__dirname, "../fixtures/logic-session.json")

export function readLogicProject(): LogicProjectFixture | null {
  if (!fs.existsSync(FIXTURE_PATH)) return null
  return JSON.parse(fs.readFileSync(FIXTURE_PATH, "utf8")) as LogicProjectFixture
}

export function writeLogicProject(fixture: LogicProjectFixture) {
  fs.mkdirSync(path.dirname(FIXTURE_PATH), { recursive: true })
  fs.writeFileSync(FIXTURE_PATH, JSON.stringify(fixture, null, 2))
}

export function clearLogicProject() {
  if (fs.existsSync(FIXTURE_PATH)) fs.unlinkSync(FIXTURE_PATH)
}

export function clearLogicSession() {
  if (fs.existsSync(LOGIC_SESSION_PATH)) fs.unlinkSync(LOGIC_SESSION_PATH)
}

export function logicSessionExists(): boolean {
  return fs.existsSync(LOGIC_SESSION_PATH)
}
