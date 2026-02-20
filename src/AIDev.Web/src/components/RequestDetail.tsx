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

  if (loading) return <div className="loading">Loading...</div>;
  if (!request) return <div className="error-banner">Request not found</div>;

  return (
    <div className="page">
      <div className="page-header">
        <Link to="/" className="btn btn-secondary">
          ← Back to List
        </Link>
        <button className="btn btn-danger" onClick={handleDelete}>
          Delete
        </button>
      </div>

      {error && <div className="error-banner">{error}</div>}

      <div className="request-detail">
        <div className="detail-header">
          <h1>
            #{request.id} — {request.title}
          </h1>
          <div className="detail-meta">
            <span
              className="badge"
              style={{
                backgroundColor: statusColors[request.status],
                color: "#fff",
              }}
            >
              {request.status}
            </span>
            <span className="badge badge-type">{request.requestType}</span>
            <span
              className="badge"
              style={{
                backgroundColor: priorityColors[request.priority],
                color: "#fff",
              }}
            >
              {request.priority}
            </span>
            {request.gitHubIssueUrl && (
              <a
                href={request.gitHubIssueUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="github-link"
              >
                GitHub Issue #{request.gitHubIssueNumber}
              </a>
            )}
          </div>
        </div>

        <div className="detail-info">
          <p>
            <strong>Submitted by:</strong> {request.submittedBy} (
            {request.submittedByEmail})
          </p>
          <p>
            <strong>Created:</strong>{" "}
            {new Date(request.createdAt).toLocaleString()}
          </p>
          <p>
            <strong>Updated:</strong>{" "}
            {new Date(request.updatedAt).toLocaleString()}
          </p>
        </div>

        <div className="detail-section">
          <h2>Description</h2>
          <p className="detail-text">{request.description}</p>
        </div>

        {request.stepsToReproduce && (
          <div className="detail-section">
            <h2>Steps to Reproduce</h2>
            <p className="detail-text">{request.stepsToReproduce}</p>
          </div>
        )}

        {request.expectedBehavior && (
          <div className="detail-section">
            <h2>Expected Behavior</h2>
            <p className="detail-text">{request.expectedBehavior}</p>
          </div>
        )}

        {request.actualBehavior && (
          <div className="detail-section">
            <h2>Actual Behavior</h2>
            <p className="detail-text">{request.actualBehavior}</p>
          </div>
        )}

        <div className="detail-section">
          <h2>Update Status</h2>
          <div className="status-buttons">
            {statusOptions.map((s) => (
              <button
                key={s}
                className={`btn btn-sm ${request.status === s ? "btn-active" : "btn-outline"}`}
                style={
                  request.status === s
                    ? { backgroundColor: statusColors[s], color: "#fff" }
                    : {}
                }
                onClick={() => handleStatusChange(s)}
              >
                {s}
              </button>
            ))}
          </div>
        </div>

        <div className="detail-section">
          <h2>Comments ({request.comments.length})</h2>
          {request.comments.length === 0 ? (
            <p className="muted">No comments yet.</p>
          ) : (
            <div className="comments-list">
              {request.comments.map((c) => (
                <div key={c.id} className="comment">
                  <div className="comment-header">
                    <strong>{c.author}</strong>
                    <span className="muted">
                      {new Date(c.createdAt).toLocaleString()}
                    </span>
                  </div>
                  <p>{c.content}</p>
                </div>
              ))}
            </div>
          )}

          <div className="add-comment">
            <textarea
              rows={3}
              value={newComment}
              onChange={(e) => setNewComment(e.target.value)}
              placeholder="Add a comment..."
            />
            <button
              className="btn btn-primary"
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
