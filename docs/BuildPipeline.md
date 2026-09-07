# AAX Warehouse Tools — Build Pipeline

The repository carries everything Azure DevOps needs to build the model from GitHub on a Microsoft-hosted agent and produce a deployable package — no build VM and no Finance and Operations installation on the agent.

## What is in the repository

| File | Purpose |
|---|---|
| `azure-pipelines.yml` | The pipeline: restore build packages, stamp the model version, build the ZPL renderer, compile the model, create and publish the deployable package. |
| `pipeline/packages.config` | The four build reference packages and their versions. |
| `pipeline/nuget.config` | The Azure Artifacts feed the packages are restored from. |

## One-time setup in Azure DevOps

1. **Install the build tasks.** From the Marketplace, add **Dynamics 365 Finance and Operations Tools** (by Microsoft) to the organization. It provides the *Update Model Version* and *Create Deployable Package* tasks the pipeline uses.

2. **Create a feed and load the build packages.**
   - In LCS, open the **Shared asset library > NuGet package** and download the four packages for the platform and application version you deploy to: *Compiler Tools*, *Platform Build Reference*, *Application Build Reference*, and *Application Suite Build Reference*.
   - Create an Azure Artifacts feed (for example `D365BuildPackages`) and push the four `.nupkg` files to it (`nuget push -Source <feed URL> -ApiKey az <package>`).
   - Edit `pipeline/nuget.config` with the feed URL, and `pipeline/packages.config` with the exact package versions you pushed. The versions checked in match the development environment the model was built on; every environment update means new packages and new versions here.
   - Give the pipeline's build identity (*Project Collection Build Service*) **Reader** access on the feed.

3. **Connect GitHub.** Project Settings > Service connections > New service connection > **GitHub**. Prefer the *Azure Pipelines* GitHub App (installed on the repository or organization); a personal access token with `repo` scope works when apps are not allowed.

4. **Create the pipeline.** Pipelines > New pipeline > **GitHub** > select the repository > **Existing Azure Pipelines YAML file** > `azure-pipelines.yml`. Save and run. On the first run, authorize the pipeline to use the service connection and the feed when prompted.

No pipeline variables are required. The version stamped on the model is `1.0.<build id>.0`; change `ModelVersion` in the YAML to follow your own scheme.

## What the pipeline does

1. Checks the repository out under `Metadata\AAXWarehouseTools` so the build tools see the standard `<Metadata>\<Model>\Descriptor` layout — the repository root *is* the model folder.
2. Restores the four build packages into the agent workspace, without version suffixes in the folder names.
3. Stamps the model descriptor with the build version (layer `USR`, matching the descriptor).
4. Builds the ZPL renderer for .NET Framework 4.7.2. The project's post-build steps merge its dependencies into one assembly and copy it into the model `bin` folder, which is where the X++ compile looks for it.
5. Compiles the model with the X++ build tasks, passing the model `bin` and `ExternalReferences` folders as reference paths so the two custom assemblies resolve.
6. Copies the two custom assemblies into the compiled model binaries and creates the deployable package.
7. Publishes the package as the `DeployablePackage` artifact.

The pipeline runs on pushes and pull requests to `main`.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Restore fails with *Unable to find version* | `pipeline/packages.config` versions do not match the packages in the feed. Align them with the pushed `.nupkg` versions. |
| Restore fails with 401 | The build identity has no access to the feed, or the feed URL in `pipeline/nuget.config` points to another organization. |
| X++ compile reports *type or namespace could not be found* for the renderer or the feature assembly | The reference paths in the X++ build step must include `<model>\bin` (renderer, produced by the previous step) and `<model>\ExternalReferences`. Check that the renderer build step succeeded. |
| *Update Model Version* finds no descriptor | The checkout path changed; `XppDescriptorSearch` expects `AAXWarehouseTools\Descriptor\*.xml` below `MetadataPath`. |
| The package is missing the renderer | The *Add custom assemblies* copy step must run before *Create deployable package*; the package only contains what is in the compiled binaries folder. |
