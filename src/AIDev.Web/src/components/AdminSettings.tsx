import { useEffect, useState } from "react";
import {
  getAdminProjects,
  syncProjects,
  updateProject,
  type Project,
} from "../services/api";

export default function AdminSettings() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successMsg, setSuccessMsg] = useState<string | null>(null);

  useEffect(() => {
    loadProjects();
  }, []);

  async function loadProjects() {
    setLoading(true);
    try {
      const data = await getAdminProjects();
      setProjects(data);
    } catch {
      setError("Failed to load projects");
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
    </div>
  );
}
