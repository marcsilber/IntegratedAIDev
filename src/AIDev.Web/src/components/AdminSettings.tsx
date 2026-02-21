import { useEffect, useState } from "react";
import {
  getAdminProjects,
  syncProjects,
  updateProject,
  getAgentConfig,
  updateAgentConfig,
  getAgentBudget,
  getArchitectConfig,
  updateArchitectConfig,
  getArchitectBudget,
  getImplementationConfig,
  updateImplementationConfig,
  getPipelineConfig,
  updatePipelineConfig,
  getStagedDeployments,
  getDeployments,
  getDeployStatus,
  triggerDeploy,
  triggerWorkflows,
  retryDeployment,
  type Project,
  type AgentConfig,
  type TokenBudget,
  type ArchitectConfig,
  type ImplementationConfig,
  type PipelineConfig,
  type StagedDeployment,
  type DeploymentTracking,
  type DeployStatus,
} from "../services/api";

export default function AdminSettings() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [agentConfig, setAgentConfig] = useState<AgentConfig | null>(null);
  const [tokenBudget, setTokenBudget] = useState<TokenBudget | null>(null);
  const [configDraft, setConfigDraft] = useState<Partial<AgentConfig>>({});
  const [architectConfig, setArchitectConfig] = useState<ArchitectConfig | null>(null);
  const [architectBudget, setArchitectBudget] = useState<TokenBudget | null>(null);
  const [architectDraft, setArchitectDraft] = useState<Partial<ArchitectConfig>>({});
  const [implConfig, setImplConfig] = useState<ImplementationConfig | null>(null);
  const [implDraft, setImplDraft] = useState<Partial<ImplementationConfig>>({});
  const [pipelineConfig, setPipelineConfig] = useState<PipelineConfig | null>(null);
  const [pipelineDraft, setPipelineDraft] = useState<Partial<PipelineConfig>>({});
  const [savingConfig, setSavingConfig] = useState(false);
  const [savingArchConfig, setSavingArchConfig] = useState(false);
  const [savingImplConfig, setSavingImplConfig] = useState(false);
  const [savingPipelineConfig, setSavingPipelineConfig] = useState(false);
  const [stagedDeploys, setStagedDeploys] = useState<StagedDeployment[]>([]);
  const [deployments, setDeployments] = useState<DeploymentTracking[]>([]);
  const [deployStatus, setDeployStatus] = useState<DeployStatus | null>(null);
  const [deploying, setDeploying] = useState(false);
  const [retrying, setRetrying] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    loadData();
  }, []);

  async function loadData() {
    setLoading(true);
    try {
      const [projectsData, configData, budgetData, archConfigData, archBudgetData, implConfigData, pipelineConfigData] = await Promise.all([
        getAdminProjects(),
        getAgentConfig().catch(() => null),
        getAgentBudget().catch(() => null),
        getArchitectConfig().catch(() => null),
        getArchitectBudget().catch(() => null),
        getImplementationConfig().catch(() => null),
        getPipelineConfig().catch(() => null),
      ]);
      setProjects(projectsData);
      setAgentConfig(configData);
      if (configData) setConfigDraft(configData);
      setTokenBudget(budgetData);
      setArchitectConfig(archConfigData);
      if (archConfigData) setArchitectDraft(archConfigData);
      setArchitectBudget(archBudgetData);
      setImplConfig(implConfigData);
      if (implConfigData) setImplDraft(implConfigData);
      setPipelineConfig(pipelineConfigData);
      if (pipelineConfigData) setPipelineDraft(pipelineConfigData);
      // Load deployment data
      const [stagedData, deployData, statusData] = await Promise.all([
        getStagedDeployments().catch(() => []),
        getDeployments().catch(() => []),
        getDeployStatus().catch(() => null),
      ]);
      setStagedDeploys(stagedData);
      setDeployments(deployData);
      setDeployStatus(statusData);
    } catch {
      setError("Failed to load settings");
    } finally {
      setLoading(false);
    }
  }

  async function handleSync() {
    setSyncing(true);
    setError(null);
    setSuccessMsg(null);
    try {
      const data = await syncProjects();
      setProjects(data);
      setSuccessMsg(`Synced ${data.length} repositories from GitHub`);
    } catch {
      setError("Failed to sync from GitHub");
    } finally {
      setSyncing(false);
    }
  }

  async function toggleActive(project: Project) {
    try {
      const updated = await updateProject(project.id, {
        isActive: !project.isActive,
      });
      setProjects((prev) =>
        prev.map((p) => (p.id === updated.id ? updated : p))
      );
    } catch {
      setError("Failed to update project");
    }
  }

  async function handleRename(project: Project) {
    const newName = prompt("New display name:", project.displayName);
    if (!newName || newName === project.displayName) return;
    try {
      const updated = await updateProject(project.id, {
        displayName: newName,
      });
      setProjects((prev) =>
        prev.map((p) => (p.id === updated.id ? updated : p))
      );
    } catch {
      setError("Failed to rename project");
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1>Admin Settings</h1>
        <button
          className="btn btn-primary"
          onClick={handleSync}
          disabled={syncing}
        >
          {syncing ? "Syncing..." : "Sync from GitHub"}
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}
      {successMsg && <div className="success-banner">{successMsg}</div>}

      <p className="admin-hint">
        Sync repositories from your GitHub account, then activate the ones you
        want users to submit requests against.
      </p>

      {loading ? (
        <div className="loading">Loading projects...</div>
      ) : projects.length === 0 ? (
        <div className="empty-state">
          <p>No projects yet. Click "Sync from GitHub" to import your repositories.</p>
        </div>
      ) : (
        <div className="request-table">
          <table>
            <thead>
              <tr>
                <th>Display Name</th>
                <th>GitHub Repo</th>
                <th>Description</th>
                <th>Requests</th>
                <th>Active</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {projects.map((p) => (
                <tr key={p.id} style={{ opacity: p.isActive ? 1 : 0.6 }}>
                  <td>
                    <strong>{p.displayName}</strong>
                  </td>
                  <td>
                    <a
                      href={`https://github.com/${p.fullName}`}
                      target="_blank"
                      rel="noopener noreferrer"
                    >
                      {p.fullName}
                    </a>
                  </td>
                  <td>{p.description || "—"}</td>
                  <td>{p.requestCount}</td>
                  <td>
                    <label className="toggle-switch">
                      <input
                        type="checkbox"
                        checked={p.isActive}
                        onChange={() => toggleActive(p)}
                      />
                      <span className="toggle-slider"></span>
                    </label>
                  </td>
                  <td>
                    <button
                      className="btn btn-secondary btn-sm"
                      onClick={() => handleRename(p)}
                    >
                      Rename
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {/* Product Owner Agent Configuration */}
      {agentConfig && (
        <div style={{ marginTop: "2rem" }}>
          <h2>Product Owner Agent</h2>

          {/* Token Budget Display */}
          {tokenBudget && (
            <div style={{ marginTop: "1rem", display: "flex", gap: "1rem", flexWrap: "wrap" }}>
              <div style={{
                flex: 1, minWidth: "220px", padding: "1rem", borderRadius: "8px",
                border: tokenBudget.dailyBudgetExceeded ? "2px solid #ef4444" : "1px solid var(--border)",
                backgroundColor: tokenBudget.dailyBudgetExceeded ? "rgba(239, 68, 68, 0.15)" : "var(--surface)"
              }}>
                <strong>Daily Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {tokenBudget.dailyTokensUsed.toLocaleString()}
                  {tokenBudget.dailyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "var(--text-muted)" }}> / {tokenBudget.dailyTokenBudget.toLocaleString()}</span>
                  )}
                </div>
                <span className="muted">{tokenBudget.dailyReviewCount} reviews today</span>
                {tokenBudget.dailyBudgetExceeded && (
                  <div style={{ color: "#ef4444", fontWeight: 600, marginTop: "0.25rem" }}>Budget exceeded</div>
                )}
              </div>
              <div style={{
                flex: 1, minWidth: "220px", padding: "1rem", borderRadius: "8px",
                border: tokenBudget.monthlyBudgetExceeded ? "2px solid #ef4444" : "1px solid var(--border)",
                backgroundColor: tokenBudget.monthlyBudgetExceeded ? "rgba(239, 68, 68, 0.15)" : "var(--surface)"
              }}>
                <strong>Monthly Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {tokenBudget.monthlyTokensUsed.toLocaleString()}
                  {tokenBudget.monthlyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "var(--text-muted)" }}> / {tokenBudget.monthlyTokenBudget.toLocaleString()}</span>
                  )}
                </div>
                <span className="muted">{tokenBudget.monthlyReviewCount} reviews this month</span>
                {tokenBudget.monthlyBudgetExceeded && (
                  <div style={{ color: "#ef4444", fontWeight: 600, marginTop: "0.25rem" }}>Budget exceeded</div>
                )}
              </div>
            </div>
          )}

          {/* Editable Config Form */}
          <div className="request-table" style={{ marginTop: "1rem" }}>
            <table>
              <thead>
                <tr>
                  <th>Setting</th>
                  <th>Value</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><strong>Status</strong></td>
                  <td>
                    <label className="toggle-switch">
                      <input
                        type="checkbox"
                        checked={configDraft.enabled ?? false}
                        onChange={(e) => setConfigDraft({ ...configDraft, enabled: e.target.checked })}
                      />
                      <span className="toggle-slider"></span>
                    </label>
                    <span style={{ marginLeft: "0.5rem" }}>{configDraft.enabled ? "Enabled" : "Disabled"}</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Model</strong></td>
                  <td><span className="muted">{agentConfig.modelName}</span></td>
                </tr>
                <tr>
                  <td><strong>Polling Interval (seconds)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={5}
                      max={3600}
                      value={configDraft.pollingIntervalSeconds ?? 30}
                      onChange={(e) => setConfigDraft({ ...configDraft, pollingIntervalSeconds: parseInt(e.target.value) || 30 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Max Reviews / Request</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={10}
                      value={configDraft.maxReviewsPerRequest ?? 3}
                      onChange={(e) => setConfigDraft({ ...configDraft, maxReviewsPerRequest: parseInt(e.target.value) || 3 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Temperature</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      max={2}
                      step={0.1}
                      value={configDraft.temperature ?? 0.3}
                      onChange={(e) => setConfigDraft({ ...configDraft, temperature: parseFloat(e.target.value) || 0.3 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Daily Token Budget</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      value={configDraft.dailyTokenBudget ?? 0}
                      onChange={(e) => setConfigDraft({ ...configDraft, dailyTokenBudget: parseInt(e.target.value) || 0 })}
                      style={{ width: "120px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>0 = no limit</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Monthly Token Budget</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      value={configDraft.monthlyTokenBudget ?? 0}
                      onChange={(e) => setConfigDraft({ ...configDraft, monthlyTokenBudget: parseInt(e.target.value) || 0 })}
                      style={{ width: "120px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>0 = no limit</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              className="btn btn-primary"
              disabled={savingConfig}
              onClick={async () => {
                setSavingConfig(true);
                setError(null);
                setSuccessMsg(null);
                try {
                  const updated = await updateAgentConfig({
                    enabled: configDraft.enabled,
                    pollingIntervalSeconds: configDraft.pollingIntervalSeconds,
                    maxReviewsPerRequest: configDraft.maxReviewsPerRequest,
                    temperature: configDraft.temperature,
                    dailyTokenBudget: configDraft.dailyTokenBudget,
                    monthlyTokenBudget: configDraft.monthlyTokenBudget,
                  });
                  setAgentConfig(updated);
                  setConfigDraft(updated);
                  setSuccessMsg("Agent configuration saved (applies until next app restart)");
                } catch {
                  setError("Failed to save agent configuration");
                } finally {
                  setSavingConfig(false);
                }
              }}
            >
              {savingConfig ? "Saving..." : "Save Configuration"}
            </button>
            <span className="muted" style={{ fontSize: "0.8rem" }}>
              Runtime changes persist until the API is restarted.
            </span>
          </div>
        </div>
      )}

      {/* Architect Agent Configuration */}
      {architectConfig && (
        <div style={{ marginTop: "2rem" }}>
          <h2>🏗️ Architect Agent</h2>

          {/* Token Budget Display */}
          {architectBudget && (
            <div style={{ marginTop: "1rem", display: "flex", gap: "1rem", flexWrap: "wrap" }}>
              <div style={{
                flex: 1, minWidth: "220px", padding: "1rem", borderRadius: "8px",
                border: architectBudget.dailyBudgetExceeded ? "2px solid #ef4444" : "1px solid var(--border)",
                backgroundColor: architectBudget.dailyBudgetExceeded ? "rgba(239, 68, 68, 0.15)" : "var(--surface)"
              }}>
                <strong>Daily Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {architectBudget.dailyTokensUsed.toLocaleString()}
                  {architectBudget.dailyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "var(--text-muted)" }}> / {architectBudget.dailyTokenBudget.toLocaleString()}</span>
                  )}
                </div>
                <span className="muted">{architectBudget.dailyReviewCount} analyses today</span>
                {architectBudget.dailyBudgetExceeded && (
                  <div style={{ color: "#ef4444", fontWeight: 600, marginTop: "0.25rem" }}>Budget exceeded</div>
                )}
              </div>
              <div style={{
                flex: 1, minWidth: "220px", padding: "1rem", borderRadius: "8px",
                border: architectBudget.monthlyBudgetExceeded ? "2px solid #ef4444" : "1px solid var(--border)",
                backgroundColor: architectBudget.monthlyBudgetExceeded ? "rgba(239, 68, 68, 0.15)" : "var(--surface)"
              }}>
                <strong>Monthly Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {architectBudget.monthlyTokensUsed.toLocaleString()}
                  {architectBudget.monthlyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "var(--text-muted)" }}> / {architectBudget.monthlyTokenBudget.toLocaleString()}</span>
                  )}
                </div>
                <span className="muted">{architectBudget.monthlyReviewCount} analyses this month</span>
                {architectBudget.monthlyBudgetExceeded && (
                  <div style={{ color: "#ef4444", fontWeight: 600, marginTop: "0.25rem" }}>Budget exceeded</div>
                )}
              </div>
            </div>
          )}

          {/* Editable Config Form */}
          <div className="request-table" style={{ marginTop: "1rem" }}>
            <table>
              <thead>
                <tr>
                  <th>Setting</th>
                  <th>Value</th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><strong>Status</strong></td>
                  <td>
                    <label className="toggle-switch">
                      <input
                        type="checkbox"
                        checked={architectDraft.enabled ?? false}
                        onChange={(e) => setArchitectDraft({ ...architectDraft, enabled: e.target.checked })}
                      />
                      <span className="toggle-slider"></span>
                    </label>
                    <span style={{ marginLeft: "0.5rem" }}>{architectDraft.enabled ? "Enabled" : "Disabled"}</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Model</strong></td>
                  <td><span className="muted">{architectConfig.modelName}</span></td>
                </tr>
                <tr>
                  <td><strong>Polling Interval (seconds)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={10}
                      max={3600}
                      value={architectDraft.pollingIntervalSeconds ?? 60}
                      onChange={(e) => setArchitectDraft({ ...architectDraft, pollingIntervalSeconds: parseInt(e.target.value) || 60 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Max Reviews / Request</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={10}
                      value={architectDraft.maxReviewsPerRequest ?? 3}
                      onChange={(e) => setArchitectDraft({ ...architectDraft, maxReviewsPerRequest: parseInt(e.target.value) || 3 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Max Files to Read</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={50}
                      value={architectDraft.maxFilesToRead ?? 20}
                      onChange={(e) => setArchitectDraft({ ...architectDraft, maxFilesToRead: parseInt(e.target.value) || 20 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Temperature</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      max={2}
                      step={0.1}
                      value={architectDraft.temperature ?? 0.2}
                      onChange={(e) => setArchitectDraft({ ...architectDraft, temperature: parseFloat(e.target.value) || 0.2 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Daily Token Budget</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      value={architectDraft.dailyTokenBudget ?? 0}
                      onChange={(e) => setArchitectDraft({ ...architectDraft, dailyTokenBudget: parseInt(e.target.value) || 0 })}
                      style={{ width: "120px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>0 = no limit</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Monthly Token Budget</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      value={architectDraft.monthlyTokenBudget ?? 0}
                      onChange={(e) => setArchitectDraft({ ...architectDraft, monthlyTokenBudget: parseInt(e.target.value) || 0 })}
                      style={{ width: "120px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>0 = no limit</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              className="btn btn-primary"
              disabled={savingArchConfig}
              onClick={async () => {
                setSavingArchConfig(true);
                setError(null);
                setSuccessMsg(null);
                try {
                  const updated = await updateArchitectConfig({
                    enabled: architectDraft.enabled,
                    pollingIntervalSeconds: architectDraft.pollingIntervalSeconds,
                    maxReviewsPerRequest: architectDraft.maxReviewsPerRequest,
                    maxFilesToRead: architectDraft.maxFilesToRead,
                    temperature: architectDraft.temperature,
                    dailyTokenBudget: architectDraft.dailyTokenBudget,
                    monthlyTokenBudget: architectDraft.monthlyTokenBudget,
                  });
                  setArchitectConfig(updated);
                  setArchitectDraft(updated);
                  setSuccessMsg("Architect configuration saved (applies until next app restart)");
                } catch {
                  setError("Failed to save architect configuration");
                } finally {
                  setSavingArchConfig(false);
                }
              }}
            >
              {savingArchConfig ? "Saving..." : "Save Configuration"}
            </button>
            <span className="muted" style={{ fontSize: "0.8rem" }}>
              Runtime changes persist until the API is restarted.
            </span>
          </div>
        </div>
      )}

      {/* Implementation Config */}
      {implConfig && (
        <div className="dashboard-card" style={{ marginTop: "1.5rem" }}>
          <h2>🤖 Copilot Implementation</h2>
          <div style={{ overflowX: "auto" }}>
            <table className="admin-table" style={{ width: "100%" }}>
              <tbody>
                <tr>
                  <td style={{ width: "200px" }}><strong>Enabled</strong></td>
                  <td>
                    <label style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                      <input
                        type="checkbox"
                        checked={implDraft.enabled ?? true}
                        onChange={(e) => setImplDraft({ ...implDraft, enabled: e.target.checked })}
                      />
                      {implDraft.enabled ? "Active" : "Disabled"}
                    </label>
                  </td>
                </tr>
                <tr>
                  <td><strong>Auto-Trigger</strong></td>
                  <td>
                    <label style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                      <input
                        type="checkbox"
                        checked={implDraft.autoTriggerOnApproval ?? true}
                        onChange={(e) => setImplDraft({ ...implDraft, autoTriggerOnApproval: e.target.checked })}
                      />
                      Auto-trigger on architect approval
                    </label>
                  </td>
                </tr>
                <tr>
                  <td><strong>Polling Interval (s)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={10}
                      value={implDraft.pollingIntervalSeconds ?? 60}
                      onChange={(e) => setImplDraft({ ...implDraft, pollingIntervalSeconds: parseInt(e.target.value) || 60 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>PR Poll Interval (s)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={30}
                      value={implDraft.prPollIntervalSeconds ?? 120}
                      onChange={(e) => setImplDraft({ ...implDraft, prPollIntervalSeconds: parseInt(e.target.value) || 120 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Max Concurrent Sessions</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={10}
                      value={implDraft.maxConcurrentSessions ?? 3}
                      onChange={(e) => setImplDraft({ ...implDraft, maxConcurrentSessions: parseInt(e.target.value) || 3 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Base Branch</strong></td>
                  <td>
                    <input
                      type="text"
                      value={implDraft.baseBranch ?? "main"}
                      onChange={(e) => setImplDraft({ ...implDraft, baseBranch: e.target.value })}
                      style={{ width: "150px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Model Override</strong></td>
                  <td>
                    <input
                      type="text"
                      value={implDraft.model ?? ""}
                      onChange={(e) => setImplDraft({ ...implDraft, model: e.target.value })}
                      placeholder="Leave empty for default"
                      style={{ width: "250px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>Max Retries</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      max={5}
                      value={implDraft.maxRetries ?? 2}
                      onChange={(e) => setImplDraft({ ...implDraft, maxRetries: parseInt(e.target.value) || 2 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              className="btn btn-primary"
              disabled={savingImplConfig}
              onClick={async () => {
                setSavingImplConfig(true);
                setError(null);
                setSuccessMsg(null);
                try {
                  const updated = await updateImplementationConfig({
                    enabled: implDraft.enabled,
                    autoTriggerOnApproval: implDraft.autoTriggerOnApproval,
                    pollingIntervalSeconds: implDraft.pollingIntervalSeconds,
                    prPollIntervalSeconds: implDraft.prPollIntervalSeconds,
                    maxConcurrentSessions: implDraft.maxConcurrentSessions,
                    baseBranch: implDraft.baseBranch,
                    model: implDraft.model,
                    maxRetries: implDraft.maxRetries,
                  });
                  setImplConfig(updated);
                  setImplDraft(updated);
                  setSuccessMsg("Implementation configuration saved (applies until next app restart)");
                } catch {
                  setError("Failed to save implementation configuration");
                } finally {
                  setSavingImplConfig(false);
                }
              }}
            >
              {savingImplConfig ? "Saving..." : "Save Configuration"}
            </button>
            <span className="muted" style={{ fontSize: "0.8rem" }}>
              Runtime changes persist until the API is restarted.
            </span>
          </div>
        </div>
      )}

      {/* Pipeline Orchestrator Configuration */}
      {pipelineConfig && (
        <div className="dashboard-card" style={{ marginTop: "1.5rem" }}>
          <h2>🔄 Pipeline Orchestrator</h2>
          <div style={{ overflowX: "auto" }}>
            <table className="admin-table" style={{ width: "100%" }}>
              <tbody>
                <tr>
                  <td style={{ width: "250px" }}><strong>Enabled</strong></td>
                  <td>
                    <label style={{ display: "flex", alignItems: "center", gap: "0.5rem" }}>
                      <input
                        type="checkbox"
                        checked={pipelineDraft.enabled ?? true}
                        onChange={(e) => setPipelineDraft({ ...pipelineDraft, enabled: e.target.checked })}
                      />
                      {pipelineDraft.enabled ? "Active" : "Disabled"}
                    </label>
                  </td>
                </tr>
                <tr>
                  <td><strong>Poll Interval (s)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={10}
                      value={pipelineDraft.pollIntervalSeconds ?? 60}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, pollIntervalSeconds: parseInt(e.target.value) || 60 })}
                      style={{ width: "80px" }}
                    />
                  </td>
                </tr>
                <tr>
                  <td><strong>NeedsClarification Stale (days)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={30}
                      value={pipelineDraft.needsClarificationStaleDays ?? 7}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, needsClarificationStaleDays: parseInt(e.target.value) || 7 })}
                      style={{ width: "80px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>days before stall alert</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>ArchitectReview Stale (days)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={14}
                      value={pipelineDraft.architectReviewStaleDays ?? 3}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, architectReviewStaleDays: parseInt(e.target.value) || 3 })}
                      style={{ width: "80px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>days before stall alert</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Approved Stale (days)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={7}
                      value={pipelineDraft.approvedStaleDays ?? 1}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, approvedStaleDays: parseInt(e.target.value) || 1 })}
                      style={{ width: "80px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>days before stall alert</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Failed Stale (hours)</strong></td>
                  <td>
                    <input
                      type="number"
                      min={1}
                      max={168}
                      value={pipelineDraft.failedStaleHours ?? 24}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, failedStaleHours: parseInt(e.target.value) || 24 })}
                      style={{ width: "80px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>hours before stall alert</span>
                  </td>
                </tr>
                <tr>
                  <td><strong>Deployment Mode</strong></td>
                  <td>
                    <select
                      value={pipelineDraft.deploymentMode ?? "Auto"}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, deploymentMode: e.target.value })}
                      style={{ padding: "0.25rem 0.5rem", background: "var(--surface)", color: "var(--text)", border: "1px solid var(--border)", borderRadius: "4px" }}
                    >
                      <option value="Auto">Auto — merge &amp; deploy immediately</option>
                      <option value="Staged">Staged — approve PRs, deploy on demand</option>
                    </select>
                  </td>
                </tr>
                <tr>
                  <td><strong>Max Deploy Retries</strong></td>
                  <td>
                    <input
                      type="number"
                      min={0}
                      max={10}
                      value={pipelineDraft.maxDeployRetries ?? 3}
                      onChange={(e) => setPipelineDraft({ ...pipelineDraft, maxDeployRetries: parseInt(e.target.value) || 3 })}
                      style={{ width: "80px" }}
                    />
                    <span className="muted" style={{ marginLeft: "0.5rem" }}>auto-retry failed deployments</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
          <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
            <button
              className="btn btn-primary"
              disabled={savingPipelineConfig}
              onClick={async () => {
                setSavingPipelineConfig(true);
                setError(null);
                setSuccessMsg(null);
                try {
                  const updated = await updatePipelineConfig({
                    enabled: pipelineDraft.enabled,
                    pollIntervalSeconds: pipelineDraft.pollIntervalSeconds,
                    needsClarificationStaleDays: pipelineDraft.needsClarificationStaleDays,
                    architectReviewStaleDays: pipelineDraft.architectReviewStaleDays,
                    approvedStaleDays: pipelineDraft.approvedStaleDays,
                    failedStaleHours: pipelineDraft.failedStaleHours,
                    deploymentMode: pipelineDraft.deploymentMode,
                    maxDeployRetries: pipelineDraft.maxDeployRetries,
                  });
                  setPipelineConfig(updated);
                  setPipelineDraft(updated);
                  setSuccessMsg("Pipeline orchestrator configuration saved (applies until next app restart)");
                } catch {
                  setError("Failed to save pipeline configuration");
                } finally {
                  setSavingPipelineConfig(false);
                }
              }}
            >
              {savingPipelineConfig ? "Saving..." : "Save Configuration"}
            </button>
            <span className="muted" style={{ fontSize: "0.8rem" }}>
              Runtime changes persist until the API is restarted.
            </span>
          </div>
        </div>
      )}

      {/* Deployments Panel */}
      <div className="dashboard-card" style={{ marginTop: "1.5rem" }}>
        <h2>🚀 Deployments</h2>

        {/* Deploy Status */}
        {deployStatus && (
          <div style={{ marginBottom: "1rem" }}>
            <div style={{ display: "flex", gap: "1rem", alignItems: "center", marginBottom: "0.5rem" }}>
              <span style={{
                display: "inline-block",
                padding: "0.2rem 0.6rem",
                borderRadius: "4px",
                fontSize: "0.8rem",
                fontWeight: 600,
                background: deployStatus.deploymentMode === "Staged" ? "var(--warning)" : "var(--primary)",
                color: "#000",
              }}>
                {deployStatus.deploymentMode} Mode
              </span>
              <button
                className="btn btn-secondary btn-sm"
                onClick={async () => {
                  const data = await getDeployStatus().catch(() => null);
                  if (data) setDeployStatus(data);
                  const staged = await getStagedDeployments().catch(() => []);
                  setStagedDeploys(staged);
                  const deps = await getDeployments().catch(() => []);
                  setDeployments(deps);
                }}
              >
                Refresh
              </button>
            </div>

            {/* Recent workflow runs */}
            <div style={{ display: "flex", gap: "2rem", flexWrap: "wrap" }}>
              <div>
                <strong style={{ fontSize: "0.85rem" }}>API Deploys</strong>
                {deployStatus.api.length === 0 ? (
                  <div className="muted" style={{ fontSize: "0.8rem" }}>No recent runs</div>
                ) : (
                  deployStatus.api.map((r) => (
                    <div key={r.runId} style={{ fontSize: "0.8rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
                      <span style={{
                        display: "inline-block",
                        width: 8, height: 8,
                        borderRadius: "50%",
                        background: r.conclusion === "success" ? "var(--success)" :
                                    r.conclusion === "failure" ? "var(--danger)" :
                                    r.status === "in_progress" ? "var(--warning)" : "var(--text-muted)",
                      }} />
                      <span>{r.status === "completed" ? r.conclusion : r.status}</span>
                      <span className="muted">{new Date(r.createdAt).toLocaleString()}</span>
                    </div>
                  ))
                )}
              </div>
              <div>
                <strong style={{ fontSize: "0.85rem" }}>Web Deploys</strong>
                {deployStatus.web.length === 0 ? (
                  <div className="muted" style={{ fontSize: "0.8rem" }}>No recent runs</div>
                ) : (
                  deployStatus.web.map((r) => (
                    <div key={r.runId} style={{ fontSize: "0.8rem", display: "flex", gap: "0.5rem", alignItems: "center" }}>
                      <span style={{
                        display: "inline-block",
                        width: 8, height: 8,
                        borderRadius: "50%",
                        background: r.conclusion === "success" ? "var(--success)" :
                                    r.conclusion === "failure" ? "var(--danger)" :
                                    r.status === "in_progress" ? "var(--warning)" : "var(--text-muted)",
                      }} />
                      <span>{r.status === "completed" ? r.conclusion : r.status}</span>
                      <span className="muted">{new Date(r.createdAt).toLocaleString()}</span>
                    </div>
                  ))
                )}
              </div>
            </div>
          </div>
        )}

        {/* Staged PRs — ready to deploy */}
        {stagedDeploys.length > 0 && (
          <div style={{ marginBottom: "1rem" }}>
            <h3 style={{ fontSize: "1rem", marginBottom: "0.5rem" }}>Staged PRs — Ready to Deploy</h3>
            <table className="admin-table" style={{ width: "100%" }}>
              <thead>
                <tr>
                  <th>Request</th>
                  <th>PR</th>
                  <th>Quality</th>
                  <th>Approved</th>
                </tr>
              </thead>
              <tbody>
                {stagedDeploys.map((sd) => (
                  <tr key={sd.requestId}>
                    <td>#{sd.requestId} — {sd.title}</td>
                    <td>
                      <a href={sd.prUrl} target="_blank" rel="noopener noreferrer" style={{ color: "var(--primary)" }}>
                        PR #{sd.prNumber}
                      </a>
                    </td>
                    <td>{sd.qualityScore}/10</td>
                    <td>{sd.approvedAt ? new Date(sd.approvedAt).toLocaleDateString() : "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
              <button
                className="btn btn-primary"
                disabled={deploying}
                onClick={async () => {
                  setDeploying(true);
                  setError(null);
                  setSuccessMsg(null);
                  try {
                    const result = await triggerDeploy();
                    setSuccessMsg(result.message);
                    // Refresh data
                    setStagedDeploys(await getStagedDeployments().catch(() => []));
                    setDeployments(await getDeployments().catch(() => []));
                    setDeployStatus(await getDeployStatus().catch(() => null));
                  } catch {
                    setError("Failed to trigger deployment");
                  } finally {
                    setDeploying(false);
                  }
                }}
              >
                {deploying ? "Deploying..." : `Deploy ${stagedDeploys.length} PR(s)`}
              </button>
            </div>
          </div>
        )}

        {stagedDeploys.length === 0 && deployStatus?.deploymentMode === "Staged" && (
          <div className="muted" style={{ marginBottom: "1rem", fontSize: "0.9rem" }}>
            No staged PRs awaiting deployment.
          </div>
        )}

        {/* Manual deploy trigger */}
        <div style={{ marginBottom: "1rem" }}>
          <button
            className="btn btn-secondary btn-sm"
            onClick={async () => {
              setError(null);
              setSuccessMsg(null);
              try {
                const result = await triggerWorkflows();
                setSuccessMsg(result.message);
                setTimeout(async () => {
                  setDeployStatus(await getDeployStatus().catch(() => null));
                }, 5000);
              } catch {
                setError("Failed to trigger workflows");
              }
            }}
          >
            Trigger Manual Redeploy
          </button>
          <span className="muted" style={{ marginLeft: "0.5rem", fontSize: "0.8rem" }}>
            Dispatch both API and Web deploy workflows (no PR merge needed)
          </span>
        </div>

        {/* Deployment history */}
        {deployments.length > 0 && (
          <div>
            <h3 style={{ fontSize: "1rem", marginBottom: "0.5rem" }}>Deployment History</h3>
            <table className="admin-table" style={{ width: "100%" }}>
              <thead>
                <tr>
                  <th>Request</th>
                  <th>PR</th>
                  <th>Status</th>
                  <th>Retries</th>
                  <th>Deployed</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {deployments.map((d) => (
                  <tr key={d.requestId}>
                    <td>#{d.requestId} — {d.title}</td>
                    <td>{d.prNumber ? `PR #${d.prNumber}` : "—"}</td>
                    <td>
                      <span style={{
                        display: "inline-block",
                        padding: "0.15rem 0.5rem",
                        borderRadius: "4px",
                        fontSize: "0.75rem",
                        fontWeight: 600,
                        background: d.deploymentStatus === "Succeeded" ? "var(--success)" :
                                    d.deploymentStatus === "Failed" ? "var(--danger)" :
                                    d.deploymentStatus === "InProgress" ? "var(--warning)" :
                                    "var(--text-muted)",
                        color: "#000",
                      }}>
                        {d.deploymentStatus}
                      </span>
                    </td>
                    <td>{d.retryCount > 0 ? d.retryCount : "—"}</td>
                    <td>{d.deployedAt ? new Date(d.deployedAt).toLocaleString() : "—"}</td>
                    <td>
                      {d.deploymentStatus === "Failed" && (
                        <button
                          className="btn btn-secondary btn-sm"
                          disabled={retrying === d.requestId}
                          onClick={async () => {
                            setRetrying(d.requestId);
                            setError(null);
                            try {
                              const result = await retryDeployment(d.requestId);
                              setSuccessMsg(result.message);
                              setDeployments(await getDeployments().catch(() => []));
                            } catch {
                              setError("Failed to retry deployment");
                            } finally {
                              setRetrying(null);
                            }
                          }}
                        >
                          {retrying === d.requestId ? "Retrying..." : "Retry"}
                        </button>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
