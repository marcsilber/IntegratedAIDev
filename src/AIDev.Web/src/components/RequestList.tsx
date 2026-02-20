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
    <div className="animate-[fadeIn_0.2s_ease-in]">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-slate-800">Requests</h1>
        <Link
          to="/new"
          className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-primary text-white hover:bg-primary-hover no-underline transition-all duration-150"
        >
          + New Request
        </Link>
      </div>

      <div className="flex gap-3 mb-6 flex-wrap">
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
          className="flex-1 min-w-[200px] px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:border-primary"
        />

        <select
          value={statusFilter || ""}
          onChange={(e) => {
            const params = new URLSearchParams(searchParams);
            if (e.target.value) params.set("status", e.target.value);
            else params.delete("status");
            setSearchParams(params);
          }}
          className="px-3 py-2 border border-slate-200 rounded-lg text-sm bg-white"
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
          className="px-3 py-2 border border-slate-200 rounded-lg text-sm bg-white"
        >
          <option value="">All Types</option>
          <option value="Bug">Bug</option>
          <option value="Feature">Feature</option>
          <option value="Enhancement">Enhancement</option>
          <option value="Question">Question</option>
        </select>
      </div>

      {error && (
        <div className="bg-red-50 text-red-800 px-4 py-3 rounded-lg mb-4 border border-red-200">
          {error}
        </div>
      )}

      {loading ? (
        <div className="text-center py-12 text-muted">Loading requests...</div>
      ) : requests.length === 0 ? (
        <div className="text-center py-12 text-muted">
          <p>No requests found.</p>
          <Link
            to="/new"
            className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-primary text-white hover:bg-primary-hover no-underline mt-4 transition-all duration-150"
          >
            Submit the first request
          </Link>
        </div>
      ) : (
        <div className="bg-white rounded-lg shadow-sm overflow-x-auto">
          <table className="w-full border-collapse">
            <thead>
              <tr>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">#</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Title</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Type</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Priority</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Status</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Submitted By</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Created</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">GitHub</th>
              </tr>
            </thead>
            <tbody>
              {requests.map((r) => (
                <tr key={r.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">{r.id}</td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <Link to={`/requests/${r.id}`} className="text-primary no-underline font-medium hover:underline">
                      {r.title}
                    </Link>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <span className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap bg-indigo-100 text-indigo-800">
                      {r.requestType}
                    </span>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <span
                      className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap text-white"
                      style={{ backgroundColor: priorityColors[r.priority] }}
                    >
                      {r.priority}
                    </span>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <span
                      className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap text-white"
                      style={{ backgroundColor: statusColors[r.status] }}
                    >
                      {r.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">{r.submittedBy}</td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">{new Date(r.createdAt).toLocaleDateString()}</td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    {r.gitHubIssueUrl ? (
                      <a
                        href={r.gitHubIssueUrl}
                        target="_blank"
                        rel="noopener noreferrer"
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
