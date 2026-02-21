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
  type Project,
  type AgentConfig,
  type TokenBudget,
  type ArchitectConfig,
  type ImplementationConfig,
  type PipelineConfig,
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
                border: tokenBudget.dailyBudgetExceeded ? "2px solid #ef4444" : "1px solid #e2e8f0",
                backgroundColor: tokenBudget.dailyBudgetExceeded ? "#fef2f2" : "#f8fafc"
              }}>
                <strong>Daily Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {tokenBudget.dailyTokensUsed.toLocaleString()}
                  {tokenBudget.dailyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "#64748b" }}> / {tokenBudget.dailyTokenBudget.toLocaleString()}</span>
                  )}
                </div>
                <span className="muted">{tokenBudget.dailyReviewCount} reviews today</span>
                {tokenBudget.dailyBudgetExceeded && (
                  <div style={{ color: "#ef4444", fontWeight: 600, marginTop: "0.25rem" }}>Budget exceeded</div>
                )}
              </div>
              <div style={{
                flex: 1, minWidth: "220px", padding: "1rem", borderRadius: "8px",
                border: tokenBudget.monthlyBudgetExceeded ? "2px solid #ef4444" : "1px solid #e2e8f0",
                backgroundColor: tokenBudget.monthlyBudgetExceeded ? "#fef2f2" : "#f8fafc"
              }}>
                <strong>Monthly Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {tokenBudget.monthlyTokensUsed.toLocaleString()}
                  {tokenBudget.monthlyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "#64748b" }}> / {tokenBudget.monthlyTokenBudget.toLocaleString()}</span>
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
                border: architectBudget.dailyBudgetExceeded ? "2px solid #ef4444" : "1px solid #e2e8f0",
                backgroundColor: architectBudget.dailyBudgetExceeded ? "#fef2f2" : "#f8fafc"
              }}>
                <strong>Daily Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {architectBudget.dailyTokensUsed.toLocaleString()}
                  {architectBudget.dailyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "#64748b" }}> / {architectBudget.dailyTokenBudget.toLocaleString()}</span>
                  )}
                </div>
                <span className="muted">{architectBudget.dailyReviewCount} analyses today</span>
                {architectBudget.dailyBudgetExceeded && (
                  <div style={{ color: "#ef4444", fontWeight: 600, marginTop: "0.25rem" }}>Budget exceeded</div>
                )}
              </div>
              <div style={{
                flex: 1, minWidth: "220px", padding: "1rem", borderRadius: "8px",
                border: architectBudget.monthlyBudgetExceeded ? "2px solid #ef4444" : "1px solid #e2e8f0",
                backgroundColor: architectBudget.monthlyBudgetExceeded ? "#fef2f2" : "#f8fafc"
              }}>
                <strong>Monthly Usage</strong>
                <div style={{ fontSize: "1.5rem", fontWeight: 600, margin: "0.25rem 0" }}>
                  {architectBudget.monthlyTokensUsed.toLocaleString()}
                  {architectBudget.monthlyTokenBudget > 0 && (
                    <span style={{ fontSize: "0.9rem", color: "#64748b" }}> / {architectBudget.monthlyTokenBudget.toLocaleString()}</span>
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
    </div>
  );
}
