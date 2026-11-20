# AAX Warehouse Tools — Build Pipeline

The repository carries everything Azure DevOps needs to build the model from GitHub on a Microsoft-hosted agent and produce deployable packages — no build VM and no Finance and Operations installation on the agent.

The build also compiles the other packages that are deployed to the same environments, and puts every package into one deployable package. Applying a deployable package replaces the environment's custom packages, so a package left out of it would be removed from the environment.

| Package | Source |
|---|---|
| `AAXWarehouseTools` | This repository |
| `AAXFOIndexSentinel` | GitHub `NathanClouseAX/AAXFOIndexSentinel`, branch `main` |
| `AAXWarehouseDimensionLink` | GitHub `NathanClouseAX/AAXWarehouseDimensionLink`, branch `master` |
| `AAXPOCloseHelper` | GitHub `NathanClouseAX/AAXPOCloseHelper`, branch `master` |
| `AAXDataEntities` | GitHub `NathanClouseAX/AAXDataEntities`, branch `master` |
| `AAXParameterWorkflow` | Azure Repos `MVP-FO`, branch `main`, folder `src/xpp/models/AAXParameterWorkflow` |

Pushes to the other repositories don't start a build. Their changes go out with the next build of this repository.

## What is in the repository

| File | Purpose |
|---|---|
| `azure-pipelines.yml` | The pipeline: check out every package, restore the X++ build packages, stamp the model versions, build the ZPL renderer, compile the packages, create and publish the deployable packages. It lists the other repositories and each package's project. |
| `pipeline/build-packages.yml` | The steps that compile each package and check that every model of each package was compiled. |
| `pipeline/packages.config` | The X++ build packages and their versions. |
| `pipeline/nuget.config` | The Azure Artifacts feed the build packages are restored from. |

## One-time setup in Azure DevOps

1. **Build tasks.** The organization needs the **Dynamics 365 Finance and Operations Tools** extension from the Marketplace. It provides the *Update Model Version* (`XppUpdateModelVersion@0`) and *Create Deployable Package* (`XppCreatePackage@2`) tasks.

2. **Build packages feed.** `pipeline/nuget.config` points at an Azure Artifacts feed holding the six packages listed in `pipeline/packages.config` (the compiler tools, the platform build reference, and the split application build references). Every id and version in `packages.config` must exist in that feed.
   - To use your own feed, download the packages for your target version from LCS (**Shared asset library > NuGet package**), push them to the feed, and update the feed URL in `nuget.config` and the versions in `packages.config`.
   - The pipeline's build identity needs **Reader** access on the feed. When the pipeline lives in a different project than the feed, add that project's *Build Service* identity to the feed permissions, and turn off **Limit job authorization scope to current project for non-release pipelines** in the pipeline project's settings — otherwise restore fails with 401.

3. **GitHub connection.** Project Settings > Service connections > New service connection > **GitHub**. Prefer the *Azure Pipelines* GitHub App; a personal access token with `repo` scope works when apps are not allowed.

4. **Create the pipeline.** Pipelines > New pipeline > **GitHub** > select the repository > **Existing Azure Pipelines YAML file** > `/azure-pipelines.yml`. Run it; on the first run, approve the pipeline's access to the service connection when prompted.

5. **Access to the other repositories.** The *Azure Pipelines* GitHub App must be allowed to read every private repository in the package table: on GitHub, **Settings > Applications > Azure Pipelines > Configure > Repository access**. The pipeline also needs permission to use the `github.com_NathanClouseAX` service connection and the `MVP-FO` repository. Grant it on each resource's **Security > Pipeline permissions**, or approve the prompts on the first run.

No pipeline variables are required.

## What the pipeline does

1. Checks the repository out under `Metadata\AAXWarehouseTools`. The repository root is the model folder, so this gives the X++ build its usual `<Metadata>\<Model>` layout. Each other GitHub repository is checked out the same way under `Metadata\<Package>`. `AAXParameterWorkflow` is copied there from the MVP-FO checkout.
2. Restores the build packages into the agent workspace, reusing a cached copy while `packages.config` is unchanged.
3. Stamps every model descriptor with the build number (`yy.MM.dd.n`).
4. Builds the ZPL renderer for .NET Framework 4.7.2. Its post-build step merges the dependencies into one assembly and places it in the model `bin` folder.
5. Compiles each package. The X++ build copies the model `bin` folder and the project's referenced assemblies into the compiled model, so both custom assemblies travel with it. It then checks that every model of each package was compiled.
6. Creates two deployable packages holding every compiled package, and publishes them, with the compile logs, as the `drop` artifact:
   - `AXDeployableRuntime_<build>.zip` — for environments managed in Lifecycle Services.
   - `CloudDeployablePackage` — for environments managed in the Power Platform admin center.

The pipeline runs on pushes and pull requests to `main`, skipping changes that only touch documentation. Manual runs can target any branch.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| *A task is missing … XppUpdateModelVersion* or *XppCreatePackage* | The Dynamics 365 Finance and Operations Tools extension is not installed in the organization, or the YAML references a task version the extension does not provide. |
| *Unable to find version* during restore | A version in `pipeline/packages.config` is not in the feed. Align it with the pushed packages. |
| 401 or 403 during restore | The build identity cannot read the feed — see step 2 above, including the job authorization scope setting. |
| The compiler cannot find `AtomicAx.Zpl.Render` or `AAXWarehouseTools.Feature.FnO` | The renderer step must succeed before the X++ step, and the X++ step's `ReferencePath` must include the compiled model `bin` folder. |
| *Update Model Version* reports no descriptor | The checkout path changed; the descriptor is expected at `Metadata\AAXWarehouseTools\Descriptor`. |
| A checkout of another repository fails with *Repository not found* or a permission prompt | The Azure Pipelines GitHub App can't read that repository, or the pipeline isn't permitted to use the service connection or the MVP-FO repository. See setup step 5. |
| *Build X++ package* fails for one of the other packages | Read that package's compile log in the `drop` artifact's `Logs` folder. A package that references Microsoft packages outside the restored build packages needs those packages added to `pipeline/packages.config`. |
