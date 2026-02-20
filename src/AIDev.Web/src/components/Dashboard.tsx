import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getDashboard, type Dashboard } from "../services/api";

export default function DashboardView() {
  const [dashboard, setDashboard] = useState<Dashboard | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    loadDashboard();
  }, []);

  async function loadDashboard() {
    try {
      const data = await getDashboard();
      setDashboard(data);
    } catch {
      setError("Failed to load dashboard");
    } finally {
      setLoading(false);
    }
  }

  if (loading) return <div className="text-center py-12 text-muted">Loading dashboard...</div>;
  if (error) return <div className="bg-red-50 text-red-800 px-4 py-3 rounded-lg border border-red-200">{error}</div>;
  if (!dashboard) return null;

  return (
    <div className="animate-[fadeIn_0.2s_ease-in]">
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-slate-800">Dashboard</h1>
        <Link
          to="/new"
          className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-primary text-white hover:bg-primary-hover no-underline transition-all duration-150"
        >
          + New Request
        </Link>
      </div>

      <div className="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-4 mb-6">
        <div className="bg-white rounded-lg shadow-sm p-5 text-center">
          <div className="text-4xl font-bold text-primary">{dashboard.totalRequests}</div>
          <div className="text-sm text-muted font-medium mt-1">Total Requests</div>
        </div>
        <div className="bg-white rounded-lg shadow-sm p-5 text-center">
          <div className="text-4xl font-bold text-primary">{dashboard.byStatus["New"] || 0}</div>
          <div className="text-sm text-muted font-medium mt-1">New</div>
        </div>
        <div className="bg-white rounded-lg shadow-sm p-5 text-center">
          <div className="text-4xl font-bold text-primary">
            {dashboard.byStatus["InProgress"] || 0}
          </div>
          <div className="text-sm text-muted font-medium mt-1">In Progress</div>
        </div>
        <div className="bg-white rounded-lg shadow-sm p-5 text-center">
          <div className="text-4xl font-bold text-primary">{dashboard.byStatus["Done"] || 0}</div>
          <div className="text-sm text-muted font-medium mt-1">Done</div>
        </div>
      </div>

      <div className="grid grid-cols-[repeat(auto-fit,minmax(250px,1fr))] gap-4">
        <div className="bg-white rounded-lg shadow-sm p-5">
          <h2 className="text-base font-semibold mb-3 text-slate-800">By Status</h2>
          <div className="flex flex-col gap-1">
            {Object.entries(dashboard.byStatus)
              .filter(([, count]) => count > 0)
              .map(([status, count]) => (
                <div key={status} className="flex justify-between py-1 text-sm">
                  <span>{status}</span>
                  <span className="font-semibold text-primary">{count}</span>
                </div>
              ))}
          </div>
        </div>

        <div className="bg-white rounded-lg shadow-sm p-5">
          <h2 className="text-base font-semibold mb-3 text-slate-800">By Type</h2>
          <div className="flex flex-col gap-1">
            {Object.entries(dashboard.byType)
              .filter(([, count]) => count > 0)
              .map(([type, count]) => (
                <div key={type} className="flex justify-between py-1 text-sm">
                  <span>{type}</span>
                  <span className="font-semibold text-primary">{count}</span>
                </div>
              ))}
          </div>
        </div>

        <div className="bg-white rounded-lg shadow-sm p-5">
          <h2 className="text-base font-semibold mb-3 text-slate-800">By Priority</h2>
          <div className="flex flex-col gap-1">
            {Object.entries(dashboard.byPriority)
              .filter(([, count]) => count > 0)
              .map(([priority, count]) => (
                <div key={priority} className="flex justify-between py-1 text-sm">
                  <span>{priority}</span>
                  <span className="font-semibold text-primary">{count}</span>
                </div>
              ))}
          </div>
        </div>
      </div>

      <div className="bg-white rounded-lg shadow-sm p-5 mt-6">
        <h2 className="text-base font-semibold mb-3 text-slate-800">Recent Requests</h2>
        {dashboard.recentRequests.length === 0 ? (
          <p className="text-muted text-sm">No requests yet.</p>
        ) : (
          <table className="w-full border-collapse">
            <thead>
              <tr>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">#</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Title</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Status</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Priority</th>
                <th className="text-left px-4 py-3 text-xs font-semibold uppercase tracking-wider text-muted border-b-2 border-slate-200">Created</th>
              </tr>
            </thead>
            <tbody>
              {dashboard.recentRequests.map((r) => (
                <tr key={r.id} className="hover:bg-slate-50">
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">{r.id}</td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <Link to={`/requests/${r.id}`} className="text-primary no-underline hover:underline">{r.title}</Link>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <span className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap">{r.status}</span>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">
                    <span className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap">{r.priority}</span>
                  </td>
                  <td className="px-4 py-3 border-b border-slate-200 text-sm">{new Date(r.createdAt).toLocaleDateString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
