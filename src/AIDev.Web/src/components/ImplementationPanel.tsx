import { useState } from "react";
import type {
  ImplementationStatus,
  CopilotImplementationStatus,
} from "../services/api";
import {
  triggerImplementation,
  reTriggerImplementation,
  rejectImplementation,
} from "../services/api";

interface ImplementationPanelProps {
  requestId: number;
  requestStatus: string;
  implementationStatus?: ImplementationStatus | null;
  onStatusChange: () => void;
}

const statusConfig: Record<
  CopilotImplementationStatus,
  { label: string; color: string; bg: string; icon: string }
> = {
  Pending: {
    label: "Copilot Starting...",
    color: "#f59e0b",
    bg: "rgba(255, 176, 32, 0.15)",
    icon: "⏳",
  },
  Working: {
    label: "Copilot Implementing",
    color: "#6366f1",
    bg: "rgba(99, 102, 241, 0.15)",
    icon: "🔨",
  },
  PrOpened: {
    label: "PR Ready for Review",
    color: "#10b981",
    bg: "rgba(16, 185, 129, 0.15)",
    icon: "✅",
  },
  ReviewApproved: {
    label: "Review Approved — Awaiting Deploy",
    color: "#10b981",
    bg: "rgba(16, 185, 129, 0.15)",
    icon: "✅",
  },
  PrMerged: {
    label: "Implementation Complete",
    color: "#059669",
    bg: "rgba(5, 150, 105, 0.15)",
    icon: "🎉",
  },
  Failed: {
    label: "Implementation Failed",
    color: "#ef4444",
    bg: "rgba(239, 68, 68, 0.15)",
    icon: "❌",
  },
};

export default function ImplementationPanel({
  requestId,
  requestStatus,
  implementationStatus,
  onStatusChange,
}: ImplementationPanelProps) {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleTrigger = async () => {
    setLoading(true);
    setError(null);
    try {
      await triggerImplementation(requestId);
      onStatusChange();
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : "Failed to trigger implementation";
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  const handleReTrigger = async () => {
    setLoading(true);
    setError(null);
    try {
      await reTriggerImplementation(requestId);
      onStatusChange();
    } catch (err: unknown) {
      const message =
        err instanceof Error
          ? err.message
          : "Failed to re-trigger implementation";
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  const handleReject = async () => {
    const reason = prompt("Reason for rejecting this implementation:");
    if (reason === null) return; // User cancelled
    setLoading(true);
    setError(null);
    try {
      await rejectImplementation(requestId, reason || "Implementation did not meet requirements");
      onStatusChange();
    } catch (err: unknown) {
      const message =
        err instanceof Error
          ? err.message
          : "Failed to reject implementation";
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  const copilotStatus = implementationStatus?.copilotStatus;
  const config = copilotStatus ? statusConfig[copilotStatus] : null;

  // Show "Ready for implementation" when request is Approved but no Copilot session
  const isReadyToTrigger =
    requestStatus === "Approved" && !copilotStatus;

  return (
    <div
      style={{
        border: "1px solid var(--border)",
        borderRadius: 8,
        padding: 16,
        marginTop: 16,
        background: "var(--surface)",
      }}
    >
      <h4 style={{ margin: "0 0 12px 0", fontSize: 16 }}>
        🚀 Implementation
      </h4>

      {/* Ready to trigger */}
      {isReadyToTrigger && (
        <div>
          <p style={{ color: "var(--text-muted)", margin: "0 0 12px 0" }}>
            This request has an approved architecture. Ready for Copilot to
            implement.
          </p>
          <button
            onClick={handleTrigger}
            disabled={loading}
            style={{
              background: "#6366f1",
              color: "white",
              border: "none",
              borderRadius: 6,
              padding: "8px 16px",
              cursor: loading ? "not-allowed" : "pointer",
              opacity: loading ? 0.7 : 1,
              fontWeight: 500,
            }}
          >
            {loading ? "Triggering..." : "🤖 Trigger Copilot Implementation"}
          </button>
        </div>
      )}

      {/* Active status display */}
      {copilotStatus && config && (
        <div>
          {/* Status badge */}
          <div
            style={{
              display: "inline-flex",
              alignItems: "center",
              gap: 6,
              padding: "4px 12px",
              borderRadius: 9999,
              background: config.bg,
              color: config.color,
              fontWeight: 600,
              fontSize: 13,
              marginBottom: 12,
            }}
          >
            <span>{config.icon}</span>
            <span>{config.label}</span>
          </div>

          {/* Progress indicator for Pending/Working */}
          {(copilotStatus === "Pending" || copilotStatus === "Working") && (
            <div
              style={{
                margin: "12px 0",
                display: "flex",
                alignItems: "center",
                gap: 8,
              }}
            >
              <div
                style={{
                  width: 16,
                  height: 16,
                  borderRadius: "50%",
                  border: "3px solid #6366f1",
                  borderTopColor: "transparent",
                  animation: "spin 1s linear infinite",
                }}
              />
              <span style={{ color: "var(--text-muted)", fontSize: 13 }}>
                {copilotStatus === "Pending"
                  ? "Copilot is starting up..."
                  : "Copilot is implementing the approved solution..."}
              </span>
            </div>
          )}

          {/* PR link */}
          {implementationStatus?.prUrl && (
            <div
              style={{
                margin: "12px 0",
                padding: 12,
                background: "var(--bg)",
                borderRadius: 6,
                border: "1px solid var(--border)",
              }}
            >
              <span style={{ fontSize: 14 }}>🔗 Pull Request </span>
              <a
                href={implementationStatus.prUrl}
                target="_blank"
                rel="noopener noreferrer"
                style={{
                  color: "#6366f1",
                  fontWeight: 500,
                  textDecoration: "none",
                }}
              >
                #{implementationStatus.prNumber}
              </a>
              {copilotStatus === "PrOpened" && (
                <span
                  style={{
                    marginLeft: 8,
                    fontSize: 12,
                    color: "#f59e0b",
                    fontWeight: 500,
                  }}
                >
                  Awaiting review
                </span>
              )}
              {copilotStatus === "PrMerged" && (
                <span
                  style={{
                    marginLeft: 8,
                    fontSize: 12,
                    color: "#10b981",
                    fontWeight: 500,
                  }}
                >
                  Merged
                </span>
              )}
            </div>
          )}

          {/* Metadata */}
          <div
            style={{
              display: "flex",
              gap: 16,
              flexWrap: "wrap",
              fontSize: 12,
              color: "var(--text-muted)",
              marginTop: 8,
            }}
          >
            {implementationStatus?.triggeredAt && (
              <span>
                Triggered:{" "}
                {new Date(implementationStatus.triggeredAt).toLocaleString()}
              </span>
            )}
            {implementationStatus?.completedAt && (
              <span>
                Completed:{" "}
                {new Date(implementationStatus.completedAt).toLocaleString()}
              </span>
            )}
            {implementationStatus?.elapsedMinutes != null && (
              <span>
                Duration: {implementationStatus.elapsedMinutes.toFixed(1)} min
              </span>
            )}
            {implementationStatus?.copilotSessionId && (
              <span>
                Session: {implementationStatus.copilotSessionId}
              </span>
            )}
          </div>

          {/* Reject button for completed implementations */}
          {(copilotStatus === "PrMerged" || (requestStatus === "Done" && copilotStatus)) && (
            <div style={{ marginTop: 12 }}>
              <p
                style={{
                  color: "var(--text-muted)",
                  fontSize: 13,
                  margin: "0 0 8px 0",
                }}
              >
                If this implementation is incorrect, you can reject it to reset
                the request back to Approved.
              </p>
              <button
                onClick={handleReject}
                disabled={loading}
                style={{
                  background: "var(--danger)",
                  color: "white",
                  border: "none",
                  borderRadius: 6,
                  padding: "8px 16px",
                  cursor: loading ? "not-allowed" : "pointer",
                  opacity: loading ? 0.7 : 1,
                  fontWeight: 500,
                }}
              >
                {loading ? "Rejecting..." : "❌ Reject Implementation"}
              </button>
            </div>
          )}

          {/* Re-trigger button for failed */}
          {copilotStatus === "Failed" && (
            <div style={{ marginTop: 12 }}>
              <p
                style={{
                  color: "#ef4444",
                  fontSize: 13,
                  margin: "0 0 8px 0",
                }}
              >
                Copilot could not complete the implementation. You can
                re-trigger to try again.
              </p>
              <button
                onClick={handleReTrigger}
                disabled={loading}
                style={{
                  background: "#f59e0b",
                  color: "white",
                  border: "none",
                  borderRadius: 6,
                  padding: "8px 16px",
                  cursor: loading ? "not-allowed" : "pointer",
                  opacity: loading ? 0.7 : 1,
                  fontWeight: 500,
                }}
              >
                {loading ? "Re-triggering..." : "🔄 Re-trigger Copilot"}
              </button>
            </div>
          )}
        </div>
      )}

      {/* Error display */}
      {error && (
        <div
          style={{
            marginTop: 8,
            padding: 8,
            background: "rgba(239, 68, 68, 0.15)",
            color: "#ef4444",
            borderRadius: 4,
            fontSize: 13,
          }}
        >
          {error}
        </div>
      )}

      {/* CSS animation for spinner */}
      <style>{`
        @keyframes spin {
          to { transform: rotate(360deg); }
        }
      `}</style>
    </div>
  );
}
