import { useEffect, useState } from "react";
import {
  getAdminProjects,
  syncProjects,
  updateProject,
  getAgentConfig,
  updateAgentConfig,
  getAgentBudget,
  type Project,
  type AgentConfig,
  type TokenBudget,
} from "../services/api";

export default function AdminSettings() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [agentConfig, setAgentConfig] = useState<AgentConfig | null>(null);
  const [tokenBudget, setTokenBudget] = useState<TokenBudget | null>(null);
  const [configDraft, setConfigDraft] = useState<Partial<AgentConfig>>({});
  const [savingConfig, setSavingConfig] = useState(false);
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
      const [projectsData, configData, budgetData] = await Promise.all([
        getAdminProjects(),
        getAgentConfig().catch(() => null),
        getAgentBudget().catch(() => null),
      ]);
      setProjects(projectsData);
      setAgentConfig(configData);
      if (configData) setConfigDraft(configData);
      setTokenBudget(budgetData);
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
    </div>
  );
}
