# Contributing to blocks-workflow-next-sub

Thank you for your interest in contributing to **blocks-workflow-next-sub**! Your contributions help improve this project for everyone. Whether you're reporting a bug, suggesting an enhancement, or submitting code changes, we welcome your input.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How to Contribute](#how-to-contribute)
  - [Reporting Issues](#reporting-issues)
  - [Submitting Pull Requests](#submitting-pull-requests)
- [Branching Strategy](#branching-strategy)
- [Git Guidelines](#git-guidelines)
- [Coding Guidelines](#coding-guidelines)
- [Code Review Process](#code-review-process)
- [License](#license)

## Code of Conduct

Please read and follow our [Code of Conduct](./CODE_OF_CONDUCT.md). By participating in this project, you agree to abide by its terms.

## How to Contribute

### Reporting Issues

If you encounter a bug or any issue, please report it by [opening an issue](https://github.com/SELISEdigitalplatforms/blocks-workflow-next-sub/issues/new) and include the following details:

- **Description**: A clear and concise description of the bug.
- **Steps to Reproduce**: Steps to replicate the issue.
- **Expected Behavior**: What should happen.
- **Actual Behavior**: What actually happens.
- **Screenshots**: If applicable, attach screenshots.
- **Environment**: Specify OS, browser, and versions.
- **Type**: Select type `Bug`
- **Project**: Select Project `Blocks Construct`

### Submitting Pull Requests

1. **Fork the Repository**: Click the "Fork" button at the top right of the repository page.
2. **Clone Your Fork**: Clone your forked repository to your local machine.
   ```bash
   git clone https://github.com/your-username/blocks-workflow-next-sub.git
   cd blocks-workflow-next-sub
   ```
3. **Create a Branch**: Create a new branch for your feature or bugfix.
   ```bash
   git checkout -b feature/your-feature-name
   ```
4. **Make Changes**: Implement your changes in the codebase.
5. **Commit Changes**: Follow the [Git Guidelines](#git-guidelines) for commit messages.
6. **Push to GitHub**: Push your changes to your forked repository.
   ```bash
   git push origin feature/your-feature-name
   ```
7. **Open a Pull Request**: Navigate to the original repository and click "New Pull Request".

## Branching Strategy

We follow **Git Flow** for branching:

- `main`: Production-ready code.
- `dev`: Active development branch.
- `feature/*`: New features branching from `dev`.
- `bugfix/*`: Bug fixes branching from `dev`.
- `hotfix/*`: Emergency fixes branching from `main`.

## Git Guidelines

- **Use the Imperative Mood**: Start commit messages with a verb in the imperative mood (e.g., "add", "fix", "update", "remove").
- **Keep Messages Short and Descriptive**: The subject line should be concise (50 characters or less) and clearly describe the change.
- **Separate Subject from Body**: If more detail is needed, separate the subject from the body with a blank line. The body should explain the "what" and "why" of the changes.
- **Lowercase Commit Message**: Keep the commit message in lowercase.
- **Avoid Ending with a Period**: Do not end the subject line with a period.
- **Reference Issues and Pull Requests**: Reference related issues or pull requests in the body of the commit message (e.g., "fixes #123" or "see pr #456").
- **Use Conventional Commits**: Follow the Conventional Commits specification for a standardized commit message format. Types include `feat`, `fix`, `docs`, `style`, `refactor` and `test`.

Example of a well-structured commit message:

```
feat(workflow): add expression-input field type to form builder - issue(#512)

- add ExpressionInputField component to node-inspector/form-builder/fields/
- register it in fields.ts field-type registry
- add parseExpression utility with unit tests
- update node schema type definitions to include expression field type
```

## Coding Guidelines

1. **Layer your code** — Add a service method first, then a hook, then a component. Never call the `http` client directly from a component or the store.
2. **Single service singleton** — All API hooks must go through `workflowService` from `services/workflow.service.ts`. Never instantiate `WorkflowService` directly in hooks or components.
3. **Always serialise nodes** — Node data must pass through `serializeNodes` before being sent to the API and through `deserializeNodes` after receiving it. Do not bypass `WorkflowService`; the serialisation is already embedded in `createWorkflow`, `updateWorkflow`, and `getWorkflowById`.
4. **Canvas state lives in the store** — All editor state (selected node, dirty flag, panel visibility, nodes/edges maps) must be managed in `useWorkflowStore`. Do not use local `useState` for state that the rest of the canvas needs to observe.
5. **Use `useWorkflow` for canvas operations** — Components should call `useWorkflow()` rather than accessing `useWorkflowStore` directly. This keeps the React Flow instance and store actions co-located through one facade.
6. **Add a node schema before adding a node type** — Every new `NodeType` value requires a corresponding schema file in `components/node-schemas/` (e.g., `node-schema-action-myNode-v1.ts`) and registration in `node-schemas.ts`. The form builder derives its fields from this schema.
7. **Name consistently** — Files use `kebab-case`. Hooks are prefixed with `use-`. Service classes are suffixed with `Service`. Node schema files follow the pattern `node-schema-<category>-<type>-<version>.ts`.
8. **Write tests alongside code** — Every new service method, hook, and utility function must have a corresponding `.test.ts` file in the same directory. Use Vitest + React Testing Library + MSW for service/hook tests and plain Vitest for utility functions.
9. **Use the shared `http` client** — Import from `@/lib/http-client`. Do not introduce alternative HTTP libraries or call `fetch` directly.

## Code Review Process

All PRs undergo review to maintain quality. Review steps:

1. **PR Submission**: Ensure PRs are small and well-documented.
2. **Automated Checks**: CI/CD will run tests and linting.
3. **Peer Review**: At least one maintainer must approve the PR.
4. **Merge Process**: Once approved, the PR is merged into `dev`.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](./LICENSE).
