"""Write the new DevPortal.razor file."""
import os, sys

content = """\x40page "/portal"
\x40inject DevPortalApiService DevPortalApi
\x40inject IToastService ToastService
\x40inject NavigationManager Navigation

<div class="row mb-4">
    <div class="col-12">
        <h4 class="mb-1">Developer Portal</h4>
        <small class="text-muted">Your central hub for repositories, work items, pipelines, and platform resources</small>
    </div>
</div>

\x40if (_loading)
{
    <div class="d-flex justify-content-center py-5">
        <div class="spinner-border text-primary" role="status">
            <span class="visually-hidden">Loading Developer Portal...</span>
        </div>
    </div>
}
else
{
    <!-- Integration Status Cards -->
    <div class="row mb-4">
        \x40foreach (var integ in (_summary?.Integrations ?? new()))
        {
            <div class="col-md-4 mb-3">
                <div class="card h-100">
                    <div class="card-body d-flex align-items-center justify-content-between">
                        <div class="d-flex align-items-center gap-3">
                            <div class="rounded-circle d-flex align-items-center justify-content-center"
                                 style="width:40px;height:40px;background:\x40(integ.Enabled ? "rgba(40,167,69,0.15)" : "rgba(108,117,125,0.15)")">
                                <i class="fa \x40GetProviderIcon(integ.Provider) \x40(integ.Enabled ? "text-success" : "text-muted")"></i>
                            </div>
                            <div>
                                <strong>\x40integ.Provider</strong>
                                <br />
                                \x40if (integ.Enabled)
                                {
                                    <small class="text-success"><i class="fa fa-check-circle me-1"></i>Connected</small>
                                    \x40if (!string.IsNullOrEmpty(integ.Organization))
                                    {
                                        <small class="text-muted ms-2">\x40integ.Organization</small>
                                    }
                                }
                                else
                                {
                                    <small class="text-muted">Not configured</small>
                                }
                            </div>
                        </div>
                        \x40if (!integ.Enabled)
                        {
                            <a href="integrations" class="btn btn-sm btn-outline-primary">Configure</a>
                        }
                    </div>
                </div>
            </div>
        }
    </div>

    <!-- Navigation Cards: Resources -->
    <h6 class="text-muted text-uppercase fw-bold mb-3"><i class="fa fa-folder-open me-2"></i>Resources</h6>
    <div class="row mb-4">
        <div class="col-md-4 mb-3">
            <div class="card clickable-card h-100" \x40onclick='() => Navigation.NavigateTo("portal/repositories")'>
                <div class="card-body text-center py-4">
                    <i class="fa fa-code-branch fa-2x text-primary mb-3"></i>
                    <h6>Repositories</h6>
                    <p class="text-muted small mb-0">Browse repos, branches, and pull requests</p>
                </div>
            </div>
        </div>
        <div class="col-md-4 mb-3">
            <div class="card clickable-card h-100" \x40onclick='() => Navigation.NavigateTo("portal/workitems")'>
                <div class="card-body text-center py-4">
                    <i class="fa fa-tasks fa-2x text-warning mb-3"></i>
                    <h6>Work Items / Issues</h6>
                    <p class="text-muted small mb-0">Track issues, bugs, user stories, and boards</p>
                </div>
            </div>
        </div>
        <div class="col-md-4 mb-3">
            <div class="card clickable-card h-100" \x40onclick='() => Navigation.NavigateTo("portal/pipelines")'>
                <div class="card-body text-center py-4">
                    <i class="fa fa-play-circle fa-2x text-success mb-3"></i>
                    <h6>Pipelines / Actions</h6>
                    <p class="text-muted small mb-0">CI/CD runs, build status, deployments</p>
                </div>
            </div>
        </div>
    </div>

    <!-- Coming Soon: Platform Capabilities -->
    <h6 class="text-muted text-uppercase fw-bold mb-3"><i class="fa fa-road me-2"></i>Platform Capabilities <span class="badge bg-info bg-opacity-25 text-info ms-2">Coming Soon</span></h6>
    <div class="row">
        <div class="col-md-3 mb-3">
            <div class="card h-100 border-dashed">
                <div class="card-body text-center py-4 opacity-50">
                    <i class="fa fa-map-signs fa-2x text-info mb-3"></i>
                    <h6>Golden Paths</h6>
                    <p class="text-muted small mb-0">Opinionated paved-road workflows for teams</p>
                </div>
            </div>
        </div>
        <div class="col-md-3 mb-3">
            <div class="card h-100 border-dashed">
                <div class="card-body text-center py-4 opacity-50">
                    <i class="fa fa-cube fa-2x text-purple mb-3"></i>
                    <h6>Templates</h6>
                    <p class="text-muted small mb-0">Service and infrastructure starter templates</p>
                </div>
            </div>
        </div>
        <div class="col-md-3 mb-3">
            <div class="card h-100 border-dashed">
                <div class="card-body text-center py-4 opacity-50">
                    <i class="fa fa-box fa-2x text-orange mb-3"></i>
                    <h6>Artifacts</h6>
                    <p class="text-muted small mb-0">Package feeds, container images, universal packages</p>
                </div>
            </div>
        </div>
        <div class="col-md-3 mb-3">
            <div class="card h-100 border-dashed">
                <div class="card-body text-center py-4 opacity-50">
                    <i class="fa fa-magic fa-2x text-primary mb-3"></i>
                    <h6>Scaffolding</h6>
                    <p class="text-muted small mb-0">Guided project bootstrapping and code generation</p>
                </div>
            </div>
        </div>
    </div>
}

<style>
    .clickable-card {
        cursor: pointer;
        transition: transform 0.15s, box-shadow 0.15s;
    }
    .clickable-card:hover {
        transform: translateY(-2px);
        box-shadow: 0 4px 12px rgba(0,0,0,0.15);
    }
    .border-dashed {
        border-style: dashed !important;
        border-color: rgba(108,117,125,0.3) !important;
    }
    .text-purple { color: #7c3aed; }
    .text-orange { color: #ea580c; }
</style>

\x40code {
    private bool _loading = true;
    private DevPortalSummary? _summary;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _summary = await DevPortalApi.GetSummaryAsync();
        }
        catch (Exception ex)
        {
            ToastService.ShowError($"Failed to load portal: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    private string GetProviderIcon(string provider) => provider switch
    {
        "GitHub" => "fa-github",
        "Azure" => "fa-cloud",
        "Azure DevOps" or "ADO Server" => "fa-project-diagram",
        _ => "fa-plug"
    };
}
"""

target = os.path.join("src", "Platform.Engineering.Copilot.Admin.Client", "Pages", "DevPortal.razor")
with open(target, "w", encoding="utf-8") as f:
    f.write(content)
print(f"Written {len(content)} chars to {target}")
