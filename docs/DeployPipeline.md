# AAX Warehouse Tools — Deploy Pipeline

The deploy pipeline sends each successful build of `main` to the **nclouse-uat** finance and operations environment, which is managed in the Power Platform admin center. It runs in the **MVP-FO** Azure DevOps project, beside the build pipeline. For the build itself, see the [build pipeline guide](BuildPipeline.md).

## What is in the repository

| File | Purpose |
|---|---|
| `pipeline/deploy-uat.yml` | The deploy pipeline: wait for the environment's checks, then deploy the build's `CloudDeployablePackage` and verify the result. |

The deployment steps are in the file itself. They are copied from `Pipelines/templates/deploy-finops-package.yml` in the MVP-FO repository, the template that deploys MVP-FO's own builds, without its notification steps. A change to that template doesn't reach this pipeline until it is copied here.

## How it runs

1. The build pipeline, `NathanClouseAX.AAXWarehouseTools`, finishes a successful build of `main`. Pull request builds and builds of other branches don't trigger a deploy.
2. The run waits on the **Nclouse-uat** environment's checks. It must come from `main`, and an approver must approve it.
3. **Submit & Wait** checks the `NClouse-UAT` service connection, waits until the environment is ready, and deploys the package. It waits up to 3 hours for the deployment to finish.
4. **Verify** reads the environment's state in the Power Platform admin center and fails the run if the deployment failed.

To deploy an earlier build, run the pipeline manually and pick the build under **Resources**.

## One-time setup in Azure DevOps

Both pipelines must be in the project that holds the environment. A pipeline can only deploy to an environment in its own project.

1. **Build pipeline.** In MVP-FO: Pipelines > New pipeline > **GitHub** > select the repository > **Existing Azure Pipelines YAML file** > `/azure-pipelines.yml`. Keep the name `NathanClouseAX.AAXWarehouseTools`, because the deploy pipeline's `source:` uses it. This also creates MVP-FO's GitHub connection.
2. **Deploy pipeline.** The same steps with `/pipeline/deploy-uat.yml`. Save it without running it.
3. **Resource access.** The deploy pipeline needs permission to use the `Nclouse-uat` environment, the `NClouse-UAT` service connection and the `PowerPlatform-Auth` variable group. Grant it on each resource's **Security > Pipeline permissions**, or approve the prompts on the first run.

The pipeline relies on what MVP-FO's own deployments already use:

- The **Power Platform Build Tools** extension in the organization (`PowerPlatformToolInstaller@2`, `PowerPlatformDeployPackage@2`).
- The `NClouse-UAT` Power Platform service connection.
- `PP_TENANT_ID`, `PP_APPLICATION_ID` and `PP_CLIENT_SECRET` in the `PowerPlatform-Auth` variable group, for a service principal registered as a Power Platform management app.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| A build of `main` succeeds but no deploy starts | The build pipeline was renamed or is in another project, so it no longer matches `source:` in `pipeline/deploy-uat.yml`. Or `pipeline/deploy-uat.yml` isn't on `main` yet. |
| One build of `main` starts two deploys | A second pipeline was created from `pipeline/deploy-uat.yml`. Disable or delete it, so that only one pipeline deploys each build. |
| The run waits with *This pipeline needs permission to access a resource* | Grant the access in setup step 3. |
| *Validate service connection* fails with *AADSTS7000215: Invalid client secret provided* | The `NClouse-UAT` service connection holds a secret that the app registration no longer accepts. Create a client secret on the app registration and paste its **Value**, not its **Secret ID**, into the service connection. If `PP_CLIENT_SECRET` in `PowerPlatform-Auth` is for the same app, update it too. |
| The run is blocked by the branch control check | The run doesn't come from `main`. Only `main` may deploy to the environment. |
| *Deploy package* can't find `TemplatePackage.dll` | The build didn't publish `drop/CloudDeployablePackage`. Check the build's *Create deployable packages* step. |
| *Environment not found in PPAC* | The environment URL in `pipeline/deploy-uat.yml` doesn't match the environment, or the service principal can't see it. |
