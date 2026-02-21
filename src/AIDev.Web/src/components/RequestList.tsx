import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  getRequests,
  type DevRequest,
  type RequestStatus,
  type RequestType,
  type Priority,
} from "../services/api";

const statusColors: Record<RequestStatus, string> = {
  New: "#3b82f6",
  NeedsClarification: "#f97316",
  Triaged: "#8b5cf6",
  Approved: "#10b981",
  InProgress: "#f59e0b",
  Done: "#22c55e",
  Rejected: "#ef4444",
};

const priorityColors: Record<Priority, string> = {
  Low: "#94a3b8",
  Medium: "#3b82f6",
  High: "#f59e0b",
  Critical: "#ef4444",
};

export default function RequestList() {
  const [requests, setRequests] = useState<DevRequest[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchParams, setSearchParams] = useSearchParams();

  const statusFilter = searchParams.get("status") as RequestStatus | null;
  const typeFilter = searchParams.get("type") as RequestType | null;
  const searchQuery = searchParams.get("search") || "";

  useEffect(() => {
    loadRequests();
  }, [statusFilter, typeFilter, searchQuery]);

  async function loadRequests() {
    setLoading(true);
    try {
      const data = await getRequests({
        status: statusFilter ?? undefined,
        type: typeFilter ?? undefined,
        search: searchQuery || undefined,
      });
      setRequests(data);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : "Failed to load requests";
      setError(message);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="page">
      <div className="page-header">
        <h1>Requests</h1>
        <Link to="/new" className="btn btn-primary">
          + New Request
        </Link>
      </div>

      <div className="filters">
        <input
          type="text"
          placeholder="Search requests..."
          value={searchQuery}
          onChange={(e) => {
            const params = new URLSearchParams(searchParams);
            if (e.target.value) params.set("search", e.target.value);
            else params.delete("search");
            setSearchParams(params);
          }}
          className="search-input"
        />

        <select
          value={statusFilter || ""}
          onChange={(e) => {
            const params = new URLSearchParams(searchParams);
            if (e.target.value) params.set("status", e.target.value);
            else params.delete("status");
            setSearchParams(params);
          }}
        >
          <option value="">All Statuses</option>
          {Object.keys(statusColors).map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>

        <select
          value={typeFilter || ""}
          onChange={(e) => {
            const params = new URLSearchParams(searchParams);
            if (e.target.value) params.set("type", e.target.value);
            else params.delete("type");
            setSearchParams(params);
          }}
        >
          <option value="">All Types</option>
          <option value="Bug">Bug</option>
          <option value="Feature">Feature</option>
          <option value="Enhancement">Enhancement</option>
          <option value="Question">Question</option>
        </select>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {loading ? (
        <div className="loading">Loading requests...</div>
      ) : requests.length === 0 ? (
        <div className="empty-state">
          <p>No requests found.</p>
          <Link to="/new" className="btn btn-primary">
            Submit the first request
          </Link>
        </div>
      ) : (
        <div className="request-table">
          <table>
            <thead>
              <tr>
                <th>#</th>
                <th>Title</th>
                <th>Project</th>
                <th>Type</th>
                <th>Priority</th>
                <th>Status</th>
                <th>Submitted By</th>
                <th>Created</th>
                <th>GitHub</th>
              </tr>
            </thead>
            <tbody>
              {requests.map((r) => (
                <tr key={r.id}>
                  <td>{r.id}</td>
                  <td>
                    <Link to={`/requests/${r.id}`} className="request-link">
                      {r.title}
                    </Link>
                  </td>
                  <td>
                    <span className="badge badge-project">{r.projectName}</span>
                  </td>
                  <td>
                    <span className="badge badge-type">{r.requestType}</span>
                  </td>
                  <td>
                    <span
                      className="badge"
                      style={{
                        backgroundColor: priorityColors[r.priority],
                        color: "#fff",
                      }}
                    >
                      {r.priority}
                    </span>
                  </td>
                  <td>
                    <span
                      className="badge"
                      style={{
                        backgroundColor: statusColors[r.status],
                        color: "#fff",
                      }}
                    >
                      {r.status}
                    </span>
                  </td>
                  <td>{r.submittedBy}</td>
                  <td>{new Date(r.createdAt).toLocaleDateString()}</td>
                  <td>
                    {r.gitHubIssueUrl ? (
                      <a
                        href={r.gitHubIssueUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="github-link"
                      >
                        #{r.gitHubIssueNumber}
                      </a>
                    ) : (
                      "—"
                    )}
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
