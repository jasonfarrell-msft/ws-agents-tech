# Work IQ local CLI mode

This dashboard supports a local-only Work IQ mode through the official CLI. It does not require an Azure app registration, client secret, or direct Work IQ REST integration.
The dashboard process never acquires delegated tokens itself in CLI mode; the Work IQ CLI session owns sign-in and token refresh.

Mission Control keeps the dashboard in explicit **Sample** mode until someone switches it to **Live**. When Live is selected and no approved Azure app registration is configured for delegated Work IQ access, the dashboard uses the locally signed-in Work IQ CLI session as the authenticated corporate-user boundary.

For safety, the CLI-backed Live path is limited to loopback/localhost requests on the machine that owns the CLI session. It is not meant to be exposed through a shared server.

## Setup

Install the CLI:

```bash
npm install -g @microsoft/workiq
```

Accept the EULA:

```bash
workiq accept-eula
```

Confirm the CLI session can access corporate data:

```bash
workiq ask -q "Reply with OK"
```

If the CLI prompts for sign-in or consent, complete that flow in the same local user session before enabling Live mode in Mission Control.

The dashboard queries with the supported CLI shape:

```bash
workiq ask -q "..."
```

When a tenant is configured, the dashboard passes it as a global option before `ask`:

```bash
workiq --tenant-id "<tenant-id>" ask -q "..."
```

To run without a global install, configure the process as `npx -y @microsoft/workiq`; the dashboard still appends `ask -q "..."`.

## Configuration

Checked-in Development settings stay sample-only. Add one of the configurations below through user secrets or another local-only override before Mission Control advertises **Live** mode.

```json
{
  "WorkIQ": {
    "Mode": "Cli",
    "TenantId": "",
    "Cli": {
      "Enabled": true,
      "ExecutablePath": "workiq",
      "TimeoutSeconds": 60,
      "AdditionalArguments": []
    }
  }
}
```

For npx:

```json
{
  "WorkIQ": {
    "Mode": "Cli",
    "Cli": {
      "Enabled": true,
      "ExecutablePath": "npx",
      "AdditionalArguments": ["-y", "@microsoft/workiq"]
    }
  }
}
```

If CLI mode is disabled, the dashboard uses deterministic sample data. Missing CLI, timeouts, non-zero exits, canceled runs, and malformed output are shown as unavailable or unknown without logging meeting content.
If CLI authentication is missing, the dashboard reports that sign-in or EULA acceptance is required.

## Optional delegated sign-in

If your team already has an approved Azure app registration with delegated Work IQ consent, configure `AzureAd` plus the `WorkIQ` endpoint/scope settings in user secrets or other local secure configuration. Mission Control then offers Live mode through the web app's own interactive corporate sign-in instead of borrowing desktop tokens. The app redirects to HTTPS before starting delegated sign-in. If that app registration or consent is unavailable, keep using the CLI-backed Live path.
