# Multi-Agent Implementation Workflow

Use the implementation, vitest, artifact_fixer, and ui_ux_reviewer agents.

Start all four agents.

The implementation agent owns application source changes.

The vitest agent should inspect `package.json`, identify the relevant npm and
Vitest commands, analyze existing coverage, add focused tests, and run them.

The artifact_fixer agent should inspect snapshots, generated files, fixtures,
lockfiles, and build artifacts affected by the implementation. It should use
existing generation scripts instead of manually editing generated output.

The ui_ux_reviewer agent should inspect completed UI changes for usability,
accessibility, responsive behavior, visual consistency, and design-system
compliance. It should report findings and recommendations without modifying
implementation unless explicitly requested.

Prevent overlapping edits:

1. Let vitest, artifact_fixer, and ui_ux_reviewer perform their initial analysis.
2. Let implementation finish its source changes.
3. Give the implementation change summary to the other agents.
4. Let vitest update and run tests.
5. Let artifact_fixer regenerate affected artifacts.
6. Let ui_ux_reviewer perform a final UI/UX review.
7. Run the final validation scripts.
8. Return one consolidated report containing:
   - Files changed
   - Tests run
   - Artifacts regenerated
   - UI/UX findings
   - Accessibility issues
   - Failures
   - Remaining risks