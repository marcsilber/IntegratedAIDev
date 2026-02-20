import { useEffect, useState } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import {
  getRequest,
  updateRequest,
  addComment,
  deleteRequest,
  type DevRequest,
  type RequestStatus,
} from "../services/api";

const statusOptions: RequestStatus[] = [
  "New",
  "Triaged",
  "Approved",
  "InProgress",
  "Done",
  "Rejected",
];

const statusColors: Record<RequestStatus, string> = {
  New: "#3b82f6",
  Triaged: "#8b5cf6",
  Approved: "#10b981",
  InProgress: "#f59e0b",
  Done: "#22c55e",
  Rejected: "#ef4444",
};

const priorityColors: Record<string, string> = {
  Low: "#94a3b8",
  Medium: "#3b82f6",
  High: "#f59e0b",
  Critical: "#ef4444",
};

export default function RequestDetail() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const [request, setRequest] = useState<DevRequest | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [newComment, setNewComment] = useState("");
  const [commentLoading, setCommentLoading] = useState(false);

  useEffect(() => {
    if (id) loadRequest(parseInt(id));
  }, [id]);

  async function loadRequest(requestId: number) {
    setLoading(true);
    try {
      const data = await getRequest(requestId);
      setRequest(data);
    } catch {
      setError("Failed to load request");
    } finally {
      setLoading(false);
    }
  }

  async function handleStatusChange(status: RequestStatus) {
    if (!request) return;
    try {
      const updated = await updateRequest(request.id, { status });
      setRequest(updated);
    } catch {
      setError("Failed to update status");
    }
  }

  async function handleAddComment() {
    if (!request || !newComment.trim()) return;
    setCommentLoading(true);
    try {
      const comment = await addComment(request.id, newComment);
      setRequest({
        ...request,
        comments: [...request.comments, comment],
      });
      setNewComment("");
    } catch {
      setError("Failed to add comment");
    } finally {
      setCommentLoading(false);
    }
  }

  async function handleDelete() {
    if (!request || !window.confirm("Delete this request permanently?")) return;
    try {
      await deleteRequest(request.id);
      navigate("/");
    } catch {
      setError("Failed to delete request");
    }
  }

  if (loading) return <div className="text-center py-12 text-muted">Loading...</div>;
  if (!request) return <div className="bg-red-50 text-red-800 px-4 py-3 rounded-lg border border-red-200">Request not found</div>;

  return (
    <div className="animate-[fadeIn_0.2s_ease-in]">
      <div className="flex items-center justify-between mb-6">
        <Link
          to="/"
          className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-slate-100 text-slate-800 border border-slate-200 hover:bg-slate-200 no-underline transition-all duration-150"
        >
          ← Back to List
        </Link>
        <button
          className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-danger text-white hover:bg-danger-hover cursor-pointer transition-all duration-150"
          onClick={handleDelete}
        >
          Delete
        </button>
      </div>

      {error && (
        <div className="bg-red-50 text-red-800 px-4 py-3 rounded-lg mb-4 border border-red-200">
          {error}
        </div>
      )}

      <div className="bg-white rounded-lg shadow-sm p-8">
        <div>
          <h1 className="text-2xl font-bold text-slate-800 mb-3">
            #{request.id} — {request.title}
          </h1>
          <div className="flex gap-2 items-center flex-wrap mb-6">
            <span
              className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap text-white"
              style={{ backgroundColor: statusColors[request.status] }}
            >
              {request.status}
            </span>
            <span className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap bg-indigo-100 text-indigo-800">
              {request.requestType}
            </span>
            <span
              className="inline-block px-2.5 py-0.5 rounded-full text-xs font-semibold whitespace-nowrap text-white"
              style={{ backgroundColor: priorityColors[request.priority] }}
            >
              {request.priority}
            </span>
            {request.gitHubIssueUrl && (
              <a
                href={request.gitHubIssueUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="text-primary no-underline font-medium hover:underline"
              >
                GitHub Issue #{request.gitHubIssueNumber}
              </a>
            )}
          </div>
        </div>

        <div className="mb-6 p-4 bg-slate-50 rounded-lg">
          <p className="mb-1 text-sm">
            <strong>Submitted by:</strong> {request.submittedBy} (
            {request.submittedByEmail})
          </p>
          <p className="mb-1 text-sm">
            <strong>Created:</strong>{" "}
            {new Date(request.createdAt).toLocaleString()}
          </p>
          <p className="mb-1 text-sm">
            <strong>Updated:</strong>{" "}
            {new Date(request.updatedAt).toLocaleString()}
          </p>
        </div>

        <div className="mt-6 pt-6 border-t border-slate-200">
          <h2 className="text-lg font-semibold mb-3 text-slate-800">Description</h2>
          <p className="whitespace-pre-wrap leading-7 text-slate-800">{request.description}</p>
        </div>

        {request.stepsToReproduce && (
          <div className="mt-6 pt-6 border-t border-slate-200">
            <h2 className="text-lg font-semibold mb-3 text-slate-800">Steps to Reproduce</h2>
            <p className="whitespace-pre-wrap leading-7 text-slate-800">{request.stepsToReproduce}</p>
          </div>
        )}

        {request.expectedBehavior && (
          <div className="mt-6 pt-6 border-t border-slate-200">
            <h2 className="text-lg font-semibold mb-3 text-slate-800">Expected Behavior</h2>
            <p className="whitespace-pre-wrap leading-7 text-slate-800">{request.expectedBehavior}</p>
          </div>
        )}

        {request.actualBehavior && (
          <div className="mt-6 pt-6 border-t border-slate-200">
            <h2 className="text-lg font-semibold mb-3 text-slate-800">Actual Behavior</h2>
            <p className="whitespace-pre-wrap leading-7 text-slate-800">{request.actualBehavior}</p>
          </div>
        )}

        <div className="mt-6 pt-6 border-t border-slate-200">
          <h2 className="text-lg font-semibold mb-3 text-slate-800">Update Status</h2>
          <div className="flex gap-2 flex-wrap">
            {statusOptions.map((s) => (
              <button
                key={s}
                className={`inline-flex items-center justify-center px-3 py-1 text-xs font-medium rounded-lg cursor-pointer transition-all duration-150 ${
                  request.status === s
                    ? "text-white border-none"
                    : "bg-transparent border border-slate-200 text-muted hover:border-primary hover:text-primary"
                }`}
                style={
                  request.status === s
                    ? { backgroundColor: statusColors[s] }
                    : {}
                }
                onClick={() => handleStatusChange(s)}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        <div className="mt-6 pt-6 border-t border-slate-200">
          <h2 className="text-lg font-semibold mb-3 text-slate-800">Comments ({request.comments.length})</h2>
          {request.comments.length === 0 ? (
            <p className="text-muted text-sm">No comments yet.</p>
          ) : (
            <div className="flex flex-col gap-3">
              {request.comments.map((c) => (
                <div key={c.id} className="p-3 bg-slate-50 rounded-lg">
                  <div className="flex justify-between mb-1 text-sm">
                    <strong>{c.author}</strong>
                    <span className="text-muted text-sm">
                      {new Date(c.createdAt).toLocaleString()}
                    </span>
                  </div>
                  <p>{c.content}</p>
                </div>
              ))}
            </div>
          )}

          <div className="mt-4 flex flex-col gap-2">
            <textarea
              rows={3}
              value={newComment}
              onChange={(e) => setNewComment(e.target.value)}
              placeholder="Add a comment..."
              className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] resize-y focus:outline-none focus:border-primary"
            />
            <button
              className="self-end inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-primary text-white hover:bg-primary-hover cursor-pointer transition-all duration-150 disabled:opacity-60 disabled:cursor-not-allowed"
              onClick={handleAddComment}
              disabled={commentLoading || !newComment.trim()}
            >
              {commentLoading ? "Adding..." : "Add Comment"}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
