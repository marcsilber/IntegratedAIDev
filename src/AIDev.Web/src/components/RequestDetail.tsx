import { useEffect, useState, useRef, useCallback } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import {
  getRequest,
  updateRequest,
  addComment,
  deleteRequest,
  uploadAttachments,
  fetchAttachmentBlob,
  downloadAttachment,
  deleteAttachment,
  overrideAgentReview,
  triggerReReview,
  type DevRequest,
  type RequestStatus,
  type Attachment,
} from "../services/api";

/** Renders an image by fetching through the authenticated API client. */
function AuthImage({ requestId, attachment }: { requestId: number; attachment: Attachment }) {
  const [blobUrl, setBlobUrl] = useState<string | null>(null);

  useEffect(() => {
    let revoke: string | null = null;
    fetchAttachmentBlob(requestId, attachment.id).then((url) => {
      revoke = url;
      setBlobUrl(url);
    }).catch(() => {});
    return () => { if (revoke) URL.revokeObjectURL(revoke); };
  }, [requestId, attachment.id]);

  if (!blobUrl) return <div className="attachment-thumb-placeholder">Loading...</div>;

  return (
    <img
      src={blobUrl}
      alt={attachment.fileName}
      className="attachment-thumb"
      onClick={() => downloadAttachment(requestId, attachment.id, attachment.fileName)}
      style={{ cursor: "pointer" }}
    />
  );
}

const statusOptions: RequestStatus[] = [
  "New",
  "NeedsClarification",
  "Triaged",
  "Approved",
  "InProgress",
  "Done",
  "Rejected",
];

const statusColors: Record<RequestStatus, string> = {
  New: "#3b82f6",
  NeedsClarification: "#f97316",
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
  const [uploadLoading, setUploadLoading] = useState(false);
  const [dragActive, setDragActive] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

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

  const handleUploadFiles = useCallback(async (files: File[]) => {
    if (!request || files.length === 0) return;
    setUploadLoading(true);
    setError(null);
    try {
      const newAttachments = await uploadAttachments(request.id, files);
      setRequest({
        ...request,
        attachments: [...(request.attachments || []), ...newAttachments],
      });
    } catch {
      setError("Failed to upload attachment(s)");
    } finally {
      setUploadLoading(false);
    }
  }, [request]);

  async function handleDeleteAttachment(attachmentId: number) {
    if (!request || !window.confirm("Delete this attachment?")) return;
    try {
      await deleteAttachment(request.id, attachmentId);
      setRequest({
        ...request,
        attachments: request.attachments.filter((a) => a.id !== attachmentId),
      });
    } catch {
      setError("Failed to delete attachment");
    }
  }

  function handlePaste(e: React.ClipboardEvent) {
    const items = e.clipboardData?.items;
    if (!items) return;
    const files: File[] = [];
    for (let i = 0; i < items.length; i++) {
      if (items[i].type.startsWith("image/")) {
        const file = items[i].getAsFile();
        if (file) files.push(file);
      }
    }
    if (files.length > 0) {
      e.preventDefault();
      handleUploadFiles(files);
    }
  }

  function handleDrop(e: React.DragEvent) {
    e.preventDefault();
    setDragActive(false);
    const files = Array.from(e.dataTransfer.files);
    if (files.length > 0) handleUploadFiles(files);
  }

  if (loading) return <div className="loading">Loading...</div>;
  if (!request) return <div className="error-banner">Request not found</div>;

  return (
    <div className="page" onPaste={handlePaste}>
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
            <strong>Project:</strong>{" "}
            <span className="badge badge-project">{request.projectName}</span>
          </p>
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

        {/* Agent Review Panel */}
        {request.latestAgentReview && (
          <div className="detail-section agent-review-panel">
            <h2>Product Owner Agent Review</h2>
            <div className="agent-review-card" style={{
              border: `2px solid ${
                request.latestAgentReview.decision === "Approve" ? "#10b981" :
                request.latestAgentReview.decision === "Reject" ? "#ef4444" : "#f97316"
              }`,
              borderRadius: "8px",
              padding: "1rem",
              backgroundColor: "#f8fafc"
            }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "0.75rem" }}>
                <span className="badge" style={{
                  backgroundColor:
                    request.latestAgentReview.decision === "Approve" ? "#10b981" :
                    request.latestAgentReview.decision === "Reject" ? "#ef4444" : "#f97316",
                  color: "#fff",
                  fontSize: "0.9rem",
                  padding: "0.3rem 0.8rem"
                }}>
                  {request.latestAgentReview.decision}
                </span>
                <span className="muted" style={{ fontSize: "0.8rem" }}>
                  {new Date(request.latestAgentReview.createdAt).toLocaleString()} · {request.latestAgentReview.modelUsed} · {request.latestAgentReview.durationMs}ms
                </span>
              </div>

              <p style={{ marginBottom: "0.75rem" }}>{request.latestAgentReview.reasoning}</p>

              <div className="agent-scores" style={{ display: "flex", gap: "1.5rem", marginBottom: "0.75rem" }}>
                <div>
                  <strong>Alignment:</strong>{" "}
                  <span style={{ color: request.latestAgentReview.alignmentScore >= 60 ? "#10b981" : "#ef4444" }}>
                    {request.latestAgentReview.alignmentScore}/100
                  </span>
                </div>
                <div>
                  <strong>Completeness:</strong>{" "}
                  <span style={{ color: request.latestAgentReview.completenessScore >= 50 ? "#10b981" : "#ef4444" }}>
                    {request.latestAgentReview.completenessScore}/100
                  </span>
                </div>
                <div>
                  <strong>Sales Alignment:</strong>{" "}
                  <span style={{ color: request.latestAgentReview.salesAlignmentScore >= 50 ? "#10b981" : "#ef4444" }}>
                    {request.latestAgentReview.salesAlignmentScore}/100
                  </span>
                </div>
              </div>

              {request.latestAgentReview.suggestedPriority && (
                <p><strong>Suggested Priority:</strong> {request.latestAgentReview.suggestedPriority}</p>
              )}

              {request.latestAgentReview.tags && (
                <p><strong>Tags:</strong> {request.latestAgentReview.tags}</p>
              )}

              <div style={{ marginTop: "0.75rem", display: "flex", gap: "0.5rem" }}>
                <button
                  className="btn btn-sm btn-secondary"
                  onClick={async () => {
                    const reason = prompt("Reason for override (optional):");
                    try {
                      await overrideAgentReview(request.latestAgentReview!.id, "Approved", reason ?? undefined);
                      loadRequest(request.id);
                    } catch { setError("Failed to override"); }
                  }}
                >
                  Override → Approve
                </button>
                <button
                  className="btn btn-sm btn-secondary"
                  onClick={async () => {
                    const reason = prompt("Reason for override (optional):");
                    try {
                      await overrideAgentReview(request.latestAgentReview!.id, "Rejected", reason ?? undefined);
                      loadRequest(request.id);
                    } catch { setError("Failed to override"); }
                  }}
                >
                  Override → Reject
                </button>
                <button
                  className="btn btn-sm btn-secondary"
                  onClick={async () => {
                    if (!window.confirm("Queue this request for a fresh agent re-review?")) return;
                    try {
                      await triggerReReview(request.id);
                      loadRequest(request.id);
                    } catch { setError("Failed to trigger re-review"); }
                  }}
                  title="Reset this request to New status so the agent reviews it again"
                >
                  Re-review
                </button>
              </div>

              <p className="muted" style={{ marginTop: "0.5rem", fontSize: "0.75rem" }}>
                Review #{request.agentReviewCount} · Tokens: {request.latestAgentReview.promptTokens + request.latestAgentReview.completionTokens}
              </p>
            </div>
          </div>
        )}

        <div className="detail-section">
          <h2>Comments ({request.comments.length})</h2>
          {request.comments.length === 0 ? (
            <p className="muted">No comments yet.</p>
          ) : (
            <div className="comments-list">
              {request.comments.map((c) => (
                <div key={c.id} className={`comment ${c.isAgentComment ? "comment-agent" : ""}`}
                  style={c.isAgentComment ? { borderLeft: "3px solid #8b5cf6", backgroundColor: "#f5f3ff" } : {}}>
                  <div className="comment-header">
                    <strong>
                      {c.isAgentComment && "🤖 "}
                      {c.author}
                    </strong>
                    <span className="muted">
                      {new Date(c.createdAt).toLocaleString()}
                    </span>
                  </div>
                  <p style={{ whiteSpace: "pre-wrap" }}>{c.content}</p>
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

        <div className="detail-section">
          <h2>Attachments ({request.attachments?.length || 0})</h2>

          {request.attachments && request.attachments.length > 0 && (
            <div className="attachments-grid">
              {request.attachments.map((a) => (
                <div key={a.id} className="attachment-card">
                  {a.contentType.startsWith("image/") ? (
                    <AuthImage requestId={request.id} attachment={a} />
                  ) : (
                    <button
                      className="attachment-file-link"
                      onClick={() => downloadAttachment(request.id, a.id, a.fileName)}
                    >
                      📎 {a.fileName}
                    </button>
                  )}
                  <div className="attachment-info">
                    <span className="muted">
                      {a.fileName} ({(a.fileSizeBytes / 1024).toFixed(0)} KB)
                    </span>
                    <button
                      className="btn btn-danger btn-sm"
                      onClick={() => handleDeleteAttachment(a.id)}
                    >
                      ✕
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}

          <div
            className={`drop-zone ${dragActive ? "drop-zone-active" : ""}`}
            onDragOver={(e) => { e.preventDefault(); setDragActive(true); }}
            onDragLeave={() => setDragActive(false)}
            onDrop={handleDrop}
            onClick={() => fileInputRef.current?.click()}
          >
            <input
              ref={fileInputRef}
              type="file"
              multiple
              accept="image/*,.pdf,.txt,.doc,.docx"
              style={{ display: "none" }}
              onChange={(e) => {
                const files = Array.from(e.target.files || []);
                handleUploadFiles(files);
                e.target.value = "";
              }}
            />
            {uploadLoading ? (
              <p>Uploading...</p>
            ) : (
              <p>
                📋 Paste an image, drag & drop files, or <strong>click to browse</strong>
                <br />
                <span className="muted">Max 5 MB per file · Images, PDF, text files</span>
              </p>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
