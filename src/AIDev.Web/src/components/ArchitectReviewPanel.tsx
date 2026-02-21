import { useState } from "react";
import {
  approveArchitectReview,
  rejectArchitectReview,
  postArchitectFeedback,
  type ArchitectReviewResponse,
  type ArchitectDecision,
} from "../services/api";

const decisionColors: Record<ArchitectDecision, string> = {
  Pending: "#f59e0b",
  Approved: "#10b981",
  Rejected: "#ef4444",
  Revised: "#8b5cf6",
};

const severityColors: Record<string, string> = {
  low: "#3b82f6",
  medium: "#f59e0b",
  high: "#ef4444",
  critical: "#dc2626",
};

interface Props {
  review: ArchitectReviewResponse;
  onUpdated: () => void;
}

export default function ArchitectReviewPanel({ review, onUpdated }: Props) {
  const [feedbackMode, setFeedbackMode] = useState(false);
  const [feedbackText, setFeedbackText] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleApprove() {
    const reason = prompt("Reason for approval (optional):");
    if (reason === null) return; // user cancelled
    setLoading(true);
    setError(null);
    try {
      await approveArchitectReview(review.id, reason || undefined);
      onUpdated();
    } catch {
      setError("Failed to approve solution");
    } finally {
      setLoading(false);
    }
  }

  async function handleReject() {
    const reason = prompt("Reason for rejection:");
    if (!reason) return;
    setLoading(true);
    setError(null);
    try {
      await rejectArchitectReview(review.id, reason);
      onUpdated();
    } catch {
      setError("Failed to reject solution");
    } finally {
      setLoading(false);
    }
  }

  async function handleFeedback() {
    if (!feedbackText.trim()) return;
    setLoading(true);
    setError(null);
    try {
      await postArchitectFeedback(review.id, feedbackText);
      setFeedbackText("");
      setFeedbackMode(false);
      onUpdated();
    } catch {
      setError("Failed to post feedback");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div
      className="architect-review-panel"
      style={{
        border: `2px solid ${decisionColors[review.decision]}`,
        borderRadius: "8px",
        padding: "1.25rem",
        backgroundColor: "var(--surface)",
        marginBottom: "1rem",
      }}
    >
      {/* Header */}
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "1rem",
        }}
      >
        <h3 style={{ margin: 0 }}>Architect Agent Solution Proposal</h3>
        <div style={{ display: "flex", gap: "0.5rem", alignItems: "center" }}>
          <span
            className="badge"
            style={{
              backgroundColor: decisionColors[review.decision],
              color: "#fff",
              padding: "0.3rem 0.8rem",
            }}
          >
            {review.decision}
          </span>
          <span
            className="badge"
            style={{ backgroundColor: "#64748b", color: "#fff" }}
          >
            {review.estimatedComplexity}
          </span>
          <span className="muted">{review.estimatedEffort}</span>
        </div>
      </div>

      {error && (
        <div className="error-banner" style={{ marginBottom: "0.75rem" }}>
          {error}
        </div>
      )}

      {/* Summary */}
      <div style={{ marginBottom: "1rem" }}>
        <strong>Summary:</strong>
        <p style={{ margin: "0.25rem 0" }}>{review.solutionSummary}</p>
      </div>

      {/* Approach */}
      <div style={{ marginBottom: "1rem" }}>
        <strong>Approach:</strong>
        <p style={{ margin: "0.25rem 0", whiteSpace: "pre-wrap" }}>
          {review.approach}
        </p>
      </div>

      {/* Impacted Files */}
      {review.impactedFiles.length > 0 && (
        <details open style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            Impacted Files ({review.impactedFiles.length})
          </summary>
          <div
            style={{
              marginTop: "0.5rem",
              backgroundColor: "var(--bg)",
              borderRadius: "4px",
              padding: "0.75rem",
              border: "1px solid var(--border)",
            }}
          >
            {review.impactedFiles.map((f, i) => (
              <div
                key={i}
                style={{
                  padding: "0.3rem 0",
                  borderBottom:
                    i < review.impactedFiles.length - 1
                      ? "1px solid var(--border)"
                      : "none",
                }}
              >
                <code style={{ fontSize: "0.85rem" }}>{f.path}</code>
                <span
                  className="badge"
                  style={{
                    marginLeft: "0.5rem",
                    backgroundColor:
                      f.action === "modify" ? "#3b82f6" : "#10b981",
                    color: "#fff",
                    fontSize: "0.7rem",
                  }}
                >
                  {f.action}
                </span>
                <span className="muted" style={{ marginLeft: "0.5rem" }}>
                  ~{f.estimatedLinesChanged} lines
                </span>
                <div
                  className="muted"
                  style={{ fontSize: "0.8rem", marginLeft: "1rem" }}
                >
                  {f.description}
                </div>
              </div>
            ))}
          </div>
        </details>
      )}

      {/* New Files */}
      {review.newFiles.length > 0 && (
        <details open style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            New Files ({review.newFiles.length})
          </summary>
          <div
            style={{
              marginTop: "0.5rem",
              backgroundColor: "var(--bg)",
              borderRadius: "4px",
              padding: "0.75rem",
              border: "1px solid var(--border)",
            }}
          >
            {review.newFiles.map((f, i) => (
              <div key={i} style={{ padding: "0.3rem 0" }}>
                <code style={{ fontSize: "0.85rem" }}>{f.path}</code>
                <span className="muted" style={{ marginLeft: "0.5rem" }}>
                  ~{f.estimatedLines} lines
                </span>
                <div
                  className="muted"
                  style={{ fontSize: "0.8rem", marginLeft: "1rem" }}
                >
                  {f.description}
                </div>
              </div>
            ))}
          </div>
        </details>
      )}

      {/* Data Migration */}
      {review.dataMigration?.required && (
        <details style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            Data Migration Required
          </summary>
          <div
            style={{
              marginTop: "0.5rem",
              backgroundColor: "rgba(255, 176, 32, 0.1)",
              borderRadius: "4px",
              padding: "0.75rem",
              border: "1px solid rgba(255, 176, 32, 0.3)",
            }}
          >
            <p>{review.dataMigration.description}</p>
            {review.dataMigration.steps?.length > 0 && (
              <ol style={{ margin: "0.5rem 0 0 1rem" }}>
                {review.dataMigration.steps.map((step, i) => (
                  <li key={i}>{step}</li>
                ))}
              </ol>
            )}
          </div>
        </details>
      )}

      {/* Breaking Changes */}
      {review.breakingChanges.length > 0 && (
        <details style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold", color: "#ef4444" }}>
            ⚠️ Breaking Changes ({review.breakingChanges.length})
          </summary>
          <div
            style={{
              marginTop: "0.5rem",
              backgroundColor: "rgba(239, 68, 68, 0.1)",
              borderRadius: "4px",
              padding: "0.75rem",
              border: "1px solid rgba(239, 68, 68, 0.3)",
            }}
          >
            <ul style={{ margin: 0, paddingLeft: "1.25rem" }}>
              {review.breakingChanges.map((bc, i) => (
                <li key={i}>{bc}</li>
              ))}
            </ul>
          </div>
        </details>
      )}

      {/* Dependency Changes */}
      {review.dependencyChanges.length > 0 && (
        <details style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            Dependency Changes ({review.dependencyChanges.length})
          </summary>
          <div
            style={{
              marginTop: "0.5rem",
              backgroundColor: "var(--bg)",
              borderRadius: "4px",
              padding: "0.75rem",
              border: "1px solid var(--border)",
            }}
          >
            {review.dependencyChanges.map((d, i) => (
              <div key={i} style={{ padding: "0.3rem 0" }}>
                <strong>{d.package}</strong>{" "}
                <span className="badge" style={{ backgroundColor: d.action === "add" ? "#10b981" : "#ef4444", color: "#fff", fontSize: "0.7rem" }}>
                  {d.action}
                </span>{" "}
                <span className="muted">v{d.version}</span>
                <div className="muted" style={{ fontSize: "0.8rem", marginLeft: "1rem" }}>{d.reason}</div>
              </div>
            ))}
          </div>
        </details>
      )}

      {/* Risks */}
      {review.risks.length > 0 && (
        <details open style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            Risks ({review.risks.length})
          </summary>
          <div
            style={{
              marginTop: "0.5rem",
              backgroundColor: "var(--bg)",
              borderRadius: "4px",
              padding: "0.75rem",
              border: "1px solid var(--border)",
            }}
          >
            {review.risks.map((r, i) => (
              <div
                key={i}
                style={{
                  padding: "0.4rem 0",
                  borderBottom:
                    i < review.risks.length - 1
                      ? "1px solid var(--border)"
                      : "none",
                }}
              >
                <span
                  className="badge"
                  style={{
                    backgroundColor: severityColors[r.severity.toLowerCase()] || "#64748b",
                    color: "#fff",
                    fontSize: "0.7rem",
                    marginRight: "0.5rem",
                  }}
                >
                  {r.severity}
                </span>
                {r.description}
                <div className="muted" style={{ fontSize: "0.8rem", marginLeft: "1rem" }}>
                  Mitigation: {r.mitigation}
                </div>
              </div>
            ))}
          </div>
        </details>
      )}

      {/* Implementation Order */}
      {review.implementationOrder.length > 0 && (
        <details style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            Implementation Order
          </summary>
          <ol style={{ margin: "0.5rem 0 0 1.25rem" }}>
            {review.implementationOrder.map((step, i) => (
              <li key={i}>{step}</li>
            ))}
          </ol>
        </details>
      )}

      {/* Testing & Architecture Notes */}
      {(review.testingNotes || review.architecturalNotes) && (
        <details style={{ marginBottom: "1rem" }}>
          <summary style={{ cursor: "pointer", fontWeight: "bold" }}>
            Notes
          </summary>
          <div style={{ marginTop: "0.5rem" }}>
            {review.testingNotes && (
              <p>
                <strong>Testing:</strong> {review.testingNotes}
              </p>
            )}
            {review.architecturalNotes && (
              <p>
                <strong>Architecture:</strong> {review.architecturalNotes}
              </p>
            )}
          </div>
        </details>
      )}

      {/* Human Feedback */}
      {review.humanFeedback && (
        <div
          style={{
            marginBottom: "1rem",
            padding: "0.75rem",
            backgroundColor: "rgba(99, 102, 241, 0.1)",
            borderRadius: "4px",
            border: "1px solid rgba(99, 102, 241, 0.3)",
          }}
        >
          <strong>Human Feedback:</strong>
          <p style={{ margin: "0.25rem 0" }}>{review.humanFeedback}</p>
          {review.approvedBy && (
            <p className="muted">
              By {review.approvedBy} on{" "}
              {review.approvedAt
                ? new Date(review.approvedAt).toLocaleString()
                : ""}
            </p>
          )}
        </div>
      )}

      {/* Action Buttons */}
      {review.decision === "Pending" && (
        <div
          style={{
            display: "flex",
            gap: "0.5rem",
            marginBottom: "1rem",
            flexWrap: "wrap",
          }}
        >
          <button
            className="btn btn-primary"
            onClick={handleApprove}
            disabled={loading}
          >
            ✅ Approve Solution
          </button>
          <button
            className="btn btn-danger"
            onClick={handleReject}
            disabled={loading}
          >
            ❌ Reject Solution
          </button>
          <button
            className="btn btn-secondary"
            onClick={() => setFeedbackMode(!feedbackMode)}
            disabled={loading}
          >
            💬 Request Changes
          </button>
        </div>
      )}

      {/* Feedback Input */}
      {feedbackMode && (
        <div style={{ marginBottom: "1rem" }}>
          <textarea
            rows={3}
            value={feedbackText}
            onChange={(e) => setFeedbackText(e.target.value)}
            placeholder="Describe what changes you'd like to the solution proposal..."
            style={{ width: "100%", marginBottom: "0.5rem" }}
          />
          <div style={{ display: "flex", gap: "0.5rem" }}>
            <button
              className="btn btn-primary btn-sm"
              onClick={handleFeedback}
              disabled={loading || !feedbackText.trim()}
            >
              {loading ? "Sending..." : "Send Feedback"}
            </button>
            <button
              className="btn btn-secondary btn-sm"
              onClick={() => {
                setFeedbackMode(false);
                setFeedbackText("");
              }}
            >
              Cancel
            </button>
          </div>
        </div>
      )}

      {/* Footer / Metadata */}
      <div
        className="muted"
        style={{
          fontSize: "0.75rem",
          borderTop: "1px solid var(--border)",
          paddingTop: "0.5rem",
          display: "flex",
          gap: "1rem",
          flexWrap: "wrap",
        }}
      >
        <span>Files analysed: {review.filesAnalysed}</span>
        <span>Tokens: {review.totalTokensUsed}</span>
        <span>Model: {review.modelUsed}</span>
        <span>Duration: {(review.totalDurationMs / 1000).toFixed(1)}s</span>
        <span>Review #{review.id}</span>
        <span>Created: {new Date(review.createdAt).toLocaleString()}</span>
      </div>
    </div>
  );
}
