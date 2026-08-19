import fs from "fs"
import path from "path"

export type WorkflowProjectFixture = {
  projectName: string
  itemId: string
  dashboardUrl: string
  workflowUrl: string
}

const FIXTURE_PATH = path.resolve(__dirname, "../fixtures/workflow-project.json")
export const WORKFLOW_SESSION_PATH = path.resolve(__dirname, "../fixtures/workflow-session.json")

export function readWorkflowProject(): WorkflowProjectFixture | null {
  if (!fs.existsSync(FIXTURE_PATH)) return null
  return JSON.parse(fs.readFileSync(FIXTURE_PATH, "utf8")) as WorkflowProjectFixture
}

export function writeWorkflowProject(fixture: WorkflowProjectFixture) {
  fs.mkdirSync(path.dirname(FIXTURE_PATH), { recursive: true })
  fs.writeFileSync(FIXTURE_PATH, JSON.stringify(fixture, null, 2))
}

export function clearWorkflowProject() {
  if (fs.existsSync(FIXTURE_PATH)) fs.unlinkSync(FIXTURE_PATH)
}

export function clearWorkflowSession() {
  if (fs.existsSync(WORKFLOW_SESSION_PATH)) fs.unlinkSync(WORKFLOW_SESSION_PATH)
}

export function workflowSessionExists(): boolean {
  return fs.existsSync(WORKFLOW_SESSION_PATH)
}
