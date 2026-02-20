import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getDashboard, getAgentStats, type Dashboard, type AgentStats } from "../services/api";

export default function DashboardView() {
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [agentStats, setAgentStats] = useState<AgentStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadDashboard();
  }, []);

  async function loadDashboard() {
    try {
      const [dashData, statsData] = await Promise.all([
        getDashboard(),
        getAgentStats().catch(() => null),
      ]);
      setDashboard(dashData);
      setAgentStats(statsData);
    } catch {
      setError("Failed to load dashboard");
    } finally {
      setLoading(false);
    }
  }

  if (loading) return <div className="loading">Loading dashboard...</div>;
  if (error) return <div className="error-banner">{error}</div>;
  if (!dashboard) return null;

  return (
    <div className="page">
      <div className="page-header">
        <h1>Dashboard</h1>
        <Link to="/new" className="btn btn-primary">
          + New Request
        </Link>
      </div>

      <div className="stats-grid">
        <div className="stat-card">
          <div className="stat-number">{dashboard.totalRequests}</div>
          <div className="stat-label">Total Requests</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{dashboard.byStatus["New"] || 0}</div>
          <div className="stat-label">New</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">
            {dashboard.byStatus["InProgress"] || 0}
          </div>
          <div className="stat-label">In Progress</div>
        </div>
        <div className="stat-card">
          <div className="stat-number">{dashboard.byStatus["Done"] || 0}</div>
          <div className="stat-label">Done</div>
        </div>
      </div>

      <div className="dashboard-grid">
        <div className="dashboard-card">
          <h2>By Status</h2>
          <div className="breakdown-list">
            {Object.entries(dashboard.byStatus)
              .filter(([, count]) => count > 0)
              .map(([status, count]) => (
                <div key={status} className="breakdown-item">
                  <span>{status}</span>
                  <span className="breakdown-count">{count}</span>
                </div>
              ))}
          </div>
        </div>

        <div className="dashboard-card">
          <h2>By Type</h2>
          <div className="breakdown-list">
            {Object.entries(dashboard.byType)
              .filter(([, count]) => count > 0)
              .map(([type, count]) => (
                <div key={type} className="breakdown-item">
                  <span>{type}</span>
                  <span className="breakdown-count">{count}</span>
                </div>
              ))}
          </div>
        </div>

        <div className="dashboard-card">
          <h2>By Priority</h2>
          <div className="breakdown-list">
            {Object.entries(dashboard.byPriority)
              .filter(([, count]) => count > 0)
              .map(([priority, count]) => (
                <div key={priority} className="breakdown-item">
                  <span>{priority}</span>
                  <span className="breakdown-count">{count}</span>
                </div>
              ))}
          </div>
        </div>
      </div>

      <div className="dashboard-card" style={{ marginTop: "1.5rem" }}>
        <h2>Recent Requests</h2>
        {dashboard.recentRequests.length === 0 ? (
          <p className="muted">No requests yet.</p>
        ) : (
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>Title</th>
                <th>Status</th>
                <th>Priority</th>
                <th>Created</th>
              </tr>
            </thead>
            <tbody>
              {dashboard.recentRequests.map((r) => (
                <tr key={r.id}>
                  <td>{r.id}</td>
                  <td>
                    <Link to={`/requests/${r.id}`}>{r.title}</Link>
                  </td>
                  <td>
                    <span className="badge">{r.status}</span>
                  </td>
                  <td>
                    <span className="badge">{r.priority}</span>
                  </td>
                  <td>{new Date(r.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {agentStats && agentStats.totalReviews > 0 && (
        <div className="dashboard-card" style={{ marginTop: "1.5rem" }}>
          <h2>🤖 Product Owner Agent</h2>
          <div className="stats-grid" style={{ marginBottom: "1rem" }}>
            <div className="stat-card">
              <div className="stat-number">{agentStats.totalReviews}</div>
              <div className="stat-label">Total Reviews</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">{agentStats.byDecision["Approve"] || 0}</div>
              <div className="stat-label">Approved</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">{agentStats.byDecision["Clarify"] || 0}</div>
              <div className="stat-label">Clarify</div>
            </div>
            <div className="stat-card">
              <div className="stat-number">{agentStats.byDecision["Reject"] || 0}</div>
              <div className="stat-label">Rejected</div>
            </div>
          </div>
          <div className="breakdown-list">
            <div className="breakdown-item">
              <span>Avg Alignment Score</span>
              <span className="breakdown-count">{agentStats.averageAlignmentScore}</span>
            </div>
            <div className="breakdown-item">
              <span>Avg Completeness Score</span>
              <span className="breakdown-count">{agentStats.averageCompletenessScore}</span>
            </div>
            <div className="breakdown-item">
              <span>Total Tokens Used</span>
              <span className="breakdown-count">{agentStats.totalTokensUsed.toLocaleString()}</span>
            </div>
            <div className="breakdown-item">
              <span>Avg Response Time</span>
              <span className="breakdown-count">{agentStats.averageDurationMs}ms</span>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
