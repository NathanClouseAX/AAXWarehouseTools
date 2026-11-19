# AAX Warehouse Tools — Build Pipeline

The repository carries everything Azure DevOps needs to build the package from GitHub on a Microsoft-hosted agent and produce deployable packages — no build VM and no Finance and Operations installation on the agent.

## What is in the repository

| File | Purpose |
|---|---|
| `azure-pipelines.yml` | The pipeline: restore the X++ build packages, stamp the model versions, build the ZPL renderer, compile the package, create and publish the deployable packages. |
| `pipeline/packages.config` | The X++ build packages and their versions. |
| `pipeline/nuget.config` | The Azure Artifacts feed the build packages are restored from. |
| `pipeline/Test-FeedAccess.ps1` | Checks that the build identity can read the feed before the restore, and names the permission to fix when it can't. |
| `pipeline/Test-PackageModels.ps1` | Checks that every model descriptor is in the compiled package before the deployable packages are created. |

## Models in the package

The `AAXWarehouseTools` package holds two models, each with a descriptor in `Descriptor\` and a folder at the repository root:

| Model | Folder | Contents |
|---|---|---|
| `AAXWarehouseTools` | `AAXWarehouseTools\` | ZPL label preview and capture, item identity display. |
| `AAXBlindReceiving` | `AAXBlindReceiving\` | Blind PO receiving for unannounced freight. |

Both models compile into `Dynamics.AX.AAXWarehouseTools.dll` and ship in the same deployable package. To add a model, add its descriptor with `<ModelModule>AAXWarehouseTools</ModelModule>` and its folder at the repository root; the pipeline needs no change.

## One-time setup in Azure DevOps

1. **Build tasks.** The organization needs the **Dynamics 365 Finance and Operations Tools** extension from the Marketplace. It provides the *Update Model Version* (`XppUpdateModelVersion@0`) and *Create Deployable Package* (`XppCreatePackage@2`) tasks.

2. **Build packages feed.** `pipeline/nuget.config` points at an Azure Artifacts feed holding the five packages listed in `pipeline/packages.config` (the compiler tools, the platform build reference, and the split application build references). Every id and version in `packages.config` must exist in that feed.
   - To use your own feed, download the packages for your target version from LCS (**Shared asset library > NuGet package**), push them to the feed, and update the feed URL in `nuget.config` and the versions in `packages.config`.
   - The pipeline's build identity needs read access to the feed. The *Check feed access* step prints the identity's name. With **Limit job authorization scope to current project for non-release pipelines** on (the default), it is `<pipeline project> Build Service (<organization>)`; with it off, it is `Project Collection Build Service (<organization>)`. The organization-level setting (**Organization settings > Pipelines > Settings**) overrides the project one.
   - Feed in the pipeline's project: open the feed's **Feed settings > Permissions > Add users/groups** and add the identity as **Feed Reader**.
   - Feed in another project: first, in the project that hosts the feed, open **Project settings > Permissions > Readers > Members** and add the identity, so it can see that project. Then add it to the feed as **Feed Reader**, as above. Without the first step the restore fails with *VS800075*, whatever the feed permissions say.

3. **GitHub connection.** Project Settings > Service connections > New service connection > **GitHub**. Prefer the *Azure Pipelines* GitHub App; a personal access token with `repo` scope works when apps are not allowed.

4. **Create the pipeline.** Pipelines > New pipeline > **GitHub** > select the repository > **Existing Azure Pipelines YAML file** > `/azure-pipelines.yml`. Run it; on the first run, approve the pipeline's access to the service connection when prompted.

No pipeline variables are required.

## What the pipeline does

1. Checks the repository out under `Metadata\AAXWarehouseTools`. The repository root is the package folder, so this gives the X++ build its usual `<Metadata>\<Package>` layout.
2. Checks that the build identity can read the feed, then restores the build packages into the agent workspace. Both steps are skipped while a cached copy for the current `packages.config` exists.
3. Stamps every model descriptor with the build number (`yy.MM.dd.n`).
4. Builds the ZPL renderer for .NET Framework 4.7.2. Its post-build step merges the dependencies into one assembly and places it in the package `bin` folder.
5. Compiles the package, which builds every model in it. The X++ build copies the package `bin` folder and the project's referenced assemblies into the compiled package, so both custom assemblies travel with it.
6. Checks that every model descriptor is in the compiled package.
7. Creates two deployable packages and publishes them, with the compile logs, as the `drop` artifact:
   - `AXDeployableRuntime_AAXWarehouseTools_<build>.zip` — for environments managed in Lifecycle Services.
   - `CloudDeployablePackage` — for environments managed in the Power Platform admin center.

The pipeline runs on pushes and pull requests to `main`, skipping changes that only touch documentation. Manual runs can target any branch.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| *A task is missing … XppUpdateModelVersion* or *XppCreatePackage* | The Dynamics 365 Finance and Operations Tools extension is not installed in the organization, or the YAML references a task version the extension does not provide. |
| *Check feed access* reports that the identity can't see the project that hosts the feed, or the restore fails with 404 *VS800075* | The build identity can't see the feed's project. Add it to that project's **Readers** group — see step 2 above. |
| *Check feed access* reports that the identity can't read the feed, or the restore fails with 401 or 403 | The build identity can see the project but not the feed. Add it to the feed as **Feed Reader** — see step 2 above. |
| *Unable to find version* during restore | A version in `pipeline/packages.config` is not in the feed. Align it with the pushed packages. |
| The compiler cannot find `AtomicAx.Zpl.Render` or `AAXWarehouseTools.Feature.FnO` | The renderer step must succeed before the X++ step, and the X++ step's `ReferencePath` must include the compiled package `bin` folder. |
| *Check package models* fails | A descriptor in `Descriptor\` names another package in `<ModelModule>`, or its model did not compile into the package. Every model in this repository must name `AAXWarehouseTools`. |
| *Update Model Version* reports no descriptor | The checkout path changed; the descriptors are expected at `Metadata\AAXWarehouseTools\Descriptor`. |
