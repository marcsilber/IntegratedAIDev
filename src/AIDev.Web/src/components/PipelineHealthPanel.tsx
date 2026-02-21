import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  getPipelineHealth,
  getStalledRequests,
  getDeployments,
  type PipelineHealth,
  type StalledRequest,
  type DeploymentTracking,
} from "../services/api";

export default function PipelineHealthPanel() {
  const [health, setHealth] = useState<PipelineHealth | null>(null);
  const [stalledRequests, setStalledRequests] = useState<StalledRequest[]>([]);
  const [deployments, setDeployments] = useState<DeploymentTracking[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showStalled, setShowStalled] = useState(false);
  const [showDeployments, setShowDeployments] = useState(false);

  useEffect(() => {
    loadData();
    const interval = setInterval(loadData, 60000);
    return () => clearInterval(interval);
  }, []);

  async function loadData() {
    try {
      const [healthData, stalledData, deployData] = await Promise.all([
        getPipelineHealth(),
        getStalledRequests(),
        getDeployments(),
      ]);
      setHealth(healthData);
      setStalledRequests(stalledData);
      setDeployments(deployData);
    } catch {
      setError("Failed to load pipeline health");
    } finally {
      setLoading(false);
    }
  }

  if (loading) return <div className="loading">Loading pipeline health...</div>;
  if (error) return <div className="error-banner">{error}</div>;
  if (!health) return null;

  const hasIssues = health.totalStalled > 0 || health.deploymentsFailed > 0;

  return (
    <div>
      {/* Health Summary Banner */}
      <div
        style={{
          padding: "1rem 1.25rem",
          borderRadius: "8px",
          border: hasIssues ? "2px solid #f59e0b" : "1px solid #10b981",
          backgroundColor: hasIssues ? "#fffbeb" : "#f0fdf4",
          marginBottom: "1rem",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
        }}
      >
        <div>
          <strong style={{ fontSize: "1.1rem" }}>
            {hasIssues ? "⚠️ Pipeline Attention Needed" : "✅ Pipeline Healthy"}
          </strong>
          <div style={{ fontSize: "0.85rem", color: "#475569", marginTop: "0.25rem" }}>
            {health.totalStalled > 0 && (
              <span style={{ marginRight: "1rem" }}>
                {health.totalStalled} stalled request{health.totalStalled !== 1 ? "s" : ""}
              </span>
            )}
            {health.deploymentsPending + health.deploymentsInProgress > 0 && (
              <span style={{ marginRight: "1rem" }}>
                {health.deploymentsPending + health.deploymentsInProgress} deployment{health.deploymentsPending + health.deploymentsInProgress !== 1 ? "s" : ""} in progress
              </span>
            )}
            {health.deploymentsSucceeded > 0 && (
              <span style={{ marginRight: "1rem" }}>
                {health.deploymentsSucceeded} deployed to UAT
              </span>
            )}
            {health.deploymentsFailed > 0 && (
              <span style={{ color: "#ef4444" }}>
                {health.deploymentsFailed} deployment failure{health.deploymentsFailed !== 1 ? "s" : ""}
              </span>
            )}
            {health.branchesOutstanding > 0 && (
              <span style={{ marginLeft: "1rem", color: "#6366f1" }}>
                {health.branchesOutstanding} branch{health.branchesOutstanding !== 1 ? "es" : ""} pending cleanup
              </span>
            )}
          </div>
        </div>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setShowStalled(!showStalled)}
            style={{ fontSize: "0.8rem" }}
          >
            {showStalled ? "Hide" : "Show"} Stalled ({health.totalStalled})
          </button>
          <button
            className="btn btn-secondary btn-sm"
            onClick={() => setShowDeployments(!showDeployments)}
            style={{ fontSize: "0.8rem" }}
          >
            {showDeployments ? "Hide" : "Show"} Deployments
          </button>
        </div>
      </div>

      {/* Stalled Requests Detail */}
      {showStalled && (
        <div className="dashboard-card" style={{ marginBottom: "1rem" }}>
          <h3>Stalled Requests</h3>
          {stalledRequests.length === 0 ? (
            <p className="muted">No stalled requests.</p>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>#</th>
                  <th>Title</th>
                  <th>Status</th>
                  <th>Reason</th>
                  <th>Days</th>
                  <th>Severity</th>
                </tr>
              </thead>
              <tbody>
                {stalledRequests.map((s) => (
                  <tr key={s.requestId}>
                    <td>{s.requestId}</td>
                    <td>
                      <Link to={`/requests/${s.requestId}`}>{s.title}</Link>
                    </td>
                    <td>
                      <span className="badge">{s.status}</span>
                    </td>
                    <td style={{ fontSize: "0.85rem" }}>{s.stallReason}</td>
                    <td>{s.daysStalled}</td>
                    <td>
                      <span
                        style={{
                          padding: "2px 8px",
                          borderRadius: "4px",
                          fontSize: "0.75rem",
                          fontWeight: 600,
                          backgroundColor: s.severity === "Critical" ? "#fef2f2" : "#fffbeb",
                          color: s.severity === "Critical" ? "#dc2626" : "#d97706",
                          border: `1px solid ${s.severity === "Critical" ? "#fca5a5" : "#fcd34d"}`,
                        }}
                      >
                        {s.severity}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {/* Deployment Tracking */}
      {showDeployments && (
        <div className="dashboard-card" style={{ marginBottom: "1rem" }}>
          <h3>Deployment Tracking</h3>
          {deployments.length === 0 ? (
            <p className="muted">No deployments tracked yet.</p>
          ) : (
            <table>
              <thead>
                <tr>
                  <th>#</th>
                  <th>Title</th>
                  <th>PR</th>
                  <th>Deploy Status</th>
                  <th>Merged</th>
                  <th>Deployed</th>
                  <th>Branch</th>
                </tr>
              </thead>
              <tbody>
                {deployments.map((d) => (
                  <tr key={d.requestId}>
                    <td>{d.requestId}</td>
                    <td>
                      <Link to={`/requests/${d.requestId}`}>{d.title}</Link>
                    </td>
                    <td>
                      {d.prNumber ? (
                        <span>#{d.prNumber}</span>
                      ) : (
                        <span className="muted">—</span>
                      )}
                    </td>
                    <td>
                      <span
                        style={{
                          padding: "2px 8px",
                          borderRadius: "4px",
                          fontSize: "0.75rem",
                          fontWeight: 600,
                          backgroundColor:
                            d.deploymentStatus === "Succeeded"
                              ? "#f0fdf4"
                              : d.deploymentStatus === "Failed"
                              ? "#fef2f2"
                              : d.deploymentStatus === "InProgress"
                              ? "#eff6ff"
                              : "#f8fafc",
                          color:
                            d.deploymentStatus === "Succeeded"
                              ? "#16a34a"
                              : d.deploymentStatus === "Failed"
                              ? "#dc2626"
                              : d.deploymentStatus === "InProgress"
                              ? "#2563eb"
                              : "#64748b",
                        }}
                      >
                        {d.deploymentStatus === "Succeeded"
                          ? "✅ Deployed"
                          : d.deploymentStatus === "Failed"
                          ? "❌ Failed"
                          : d.deploymentStatus === "InProgress"
                          ? "🔄 Deploying"
                          : d.deploymentStatus === "Pending"
                          ? "⏳ Pending"
                          : "—"}
                      </span>
                    </td>
                    <td style={{ fontSize: "0.85rem" }}>
                      {d.mergedAt
                        ? new Date(d.mergedAt).toLocaleString()
                        : "—"}
                    </td>
                    <td style={{ fontSize: "0.85rem" }}>
                      {d.deployedAt
                        ? new Date(d.deployedAt).toLocaleString()
                        : "—"}
                    </td>
                    <td style={{ fontSize: "0.8rem" }}>
                      {d.branchDeleted ? (
                        <span style={{ color: "#10b981" }}>✓ Cleaned</span>
                      ) : d.branchName ? (
                        <span style={{ color: "#f59e0b" }}>{d.branchName}</span>
                      ) : (
                        <span className="muted">—</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      )}

      {/* Quick Stats Grid */}
      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-number" style={{ color: health.totalStalled > 0 ? "#d97706" : "#10b981" }}>
            {health.totalStalled}
          </div>
          <div className="stat-label">Stalled</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{health.deploymentsSucceeded}</div>
          <div className="stat-label">Deployed</div>
        </div>
        <div className="stat-card">
          <div className="stat-number" style={{ color: health.deploymentsFailed > 0 ? "#dc2626" : undefined }}>
            {health.deploymentsFailed}
          </div>
          <div className="stat-label">Deploy Failures</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{health.branchesDeleted}</div>
          <div className="stat-label">Branches Cleaned</div>
        </div>
      </div>
    </div>
  );
}
