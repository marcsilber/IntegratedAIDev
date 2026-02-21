import { useState, useEffect, useRef, useCallback, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import {
  createRequest,
  getProjects,
  type CreateRequest,
  type RequestType,
  type Priority,
  type Project,
} from "../services/api";

const requestTypes: RequestType[] = [
  "Bug",
  "Feature",
  "Enhancement",
  "Question",
];
const priorities: Priority[] = ["Low", "Medium", "High", "Critical"];

export default function RequestForm() {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [projects, setProjects] = useState<Project[]>([]);
  const [projectsLoading, setProjectsLoading] = useState(true);
  const [attachments, setAttachments] = useState<File[]>([]);
  const [hoveredPreview, setHoveredPreview] = useState<number | null>(null);
  const [previewUrls, setPreviewUrls] = useState<string[]>([]);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const urls = attachments.map((f) =>
      f.type.startsWith("image/") ? URL.createObjectURL(f) : ""
    );
    setPreviewUrls(urls);
    return () => urls.forEach((u) => u && URL.revokeObjectURL(u));
  }, [attachments]);

  const addFiles = useCallback((files: File[]) => {
    setAttachments((prev) => {
      const existing = new Set(prev.map((f) => f.name));
      return [...prev, ...files.filter((f) => !existing.has(f.name))];
    });
  }, []);

  const handlePaste = useCallback((e: React.ClipboardEvent) => {
    const items = e.clipboardData?.items;
    if (!items) return;
    const imageFiles: File[] = [];
    for (let i = 0; i < items.length; i++) {
      if (items[i].type.startsWith("image/")) {
        const file = items[i].getAsFile();
        if (file) {
          // Give each pasted image a unique name so multiple pastes are not
          // deduplicated when the OS assigns the same default name (e.g. "image.png").
          const extFromName = file.name.includes(".") ? file.name.split(".").pop() : undefined;
          const extFromMime = file.type.split("/")[1]?.replace(/\+.*$/, "");
          const ext = extFromName ?? extFromMime ?? "png";
          const unique = new File([file], `pasted-image-${crypto.randomUUID()}.${ext}`, { type: file.type });
          imageFiles.push(unique);
        }
      }
    }
    if (imageFiles.length > 0) {
      e.preventDefault();
      addFiles(imageFiles);
    }
  }, [addFiles]);

  const [form, setForm] = useState<CreateRequest>({
    projectId: 0,
    title: "",
    description: "",
    requestType: "Bug",
    priority: "Medium",
    stepsToReproduce: "",
    expectedBehavior: "",
    actualBehavior: "",
  });

  useEffect(() => {
    getProjects()
      .then((data) => {
        setProjects(data);
        if (data.length === 1) {
          setForm((f) => ({ ...f, projectId: data[0].id }));
        }
      })
      .catch(() => setError("Failed to load projects"))
      .finally(() => setProjectsLoading(false));
  }, []);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError(null);

    try {
      const created = await createRequest(form, attachments.length > 0 ? attachments : undefined);
      navigate(`/requests/${created.id}`);
    } catch (err: unknown) {
      const message =
        err instanceof Error ? err.message : "Failed to create request";
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="page" onPaste={handlePaste}>
      <h1>Submit New Request</h1>

      {error && <div className="error-banner">{error}</div>}

      <form onSubmit={handleSubmit} className="request-form">
        <div className="form-group">
          <label htmlFor="project">Project *</label>
          {projectsLoading ? (
            <p>Loading projects...</p>
          ) : projects.length === 0 ? (
            <p className="error-banner">
              No active projects. An admin must sync and enable projects first.
            </p>
          ) : (
            <select
              id="project"
              required
              value={form.projectId}
              onChange={(e) =>
                setForm({ ...form, projectId: Number(e.target.value) })
              }
            >
              <option value={0} disabled>
                Select a project...
              </option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.displayName} ({p.fullName})
                </option>
              ))}
            </select>
          )}
        </div>

        <div className="form-group">
          <label htmlFor="title">Title *</label>
          <input
            id="title"
            type="text"
            required
            maxLength={200}
            value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
            placeholder="Short summary of the issue or request"
          />
        </div>

        <div className="form-row">
          <div className="form-group">
            <label htmlFor="requestType">Type</label>
            <select
              id="requestType"
              value={form.requestType}
              onChange={(e) =>
                setForm({
                  ...form,
                  requestType: e.target.value as RequestType,
                })
              }
            >
              {requestTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </div>

          <div className="form-group">
            <label htmlFor="priority">Priority</label>
            <select
              id="priority"
              value={form.priority}
              onChange={(e) =>
                setForm({ ...form, priority: e.target.value as Priority })
              }
            >
              {priorities.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="form-group">
          <label htmlFor="description">Description *</label>
          <textarea
            id="description"
            required
            rows={5}
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            placeholder="Detailed explanation of the issue or feature request"
          />
        </div>

        {form.requestType === "Bug" && (
          <>
            <div className="form-group">
              <label htmlFor="stepsToReproduce">Steps to Reproduce</label>
              <textarea
                id="stepsToReproduce"
                rows={4}
                value={form.stepsToReproduce}
                onChange={(e) =>
                  setForm({ ...form, stepsToReproduce: e.target.value })
                }
                placeholder="1. Go to...&#10;2. Click on...&#10;3. Observe..."
              />
            </div>

            <div className="form-group">
              <label htmlFor="expectedBehavior">Expected Behavior</label>
              <textarea
                id="expectedBehavior"
                rows={3}
                value={form.expectedBehavior}
                onChange={(e) =>
                  setForm({ ...form, expectedBehavior: e.target.value })
                }
                placeholder="What should happen?"
              />
            </div>

            <div className="form-group">
              <label htmlFor="actualBehavior">Actual Behavior</label>
              <textarea
                id="actualBehavior"
                rows={3}
                value={form.actualBehavior}
                onChange={(e) =>
                  setForm({ ...form, actualBehavior: e.target.value })
                }
                placeholder="What actually happens?"
              />
            </div>
          </>
        )}

        <div className="form-group">
          <label>Attachments</label>
          <input
            ref={fileInputRef}
            type="file"
            multiple
            accept="image/*,.pdf,.txt,.doc,.docx"
            style={{ display: "none" }}
            onChange={(e) => {
              const selected = Array.from(e.target.files ?? []);
              addFiles(selected);
              if (fileInputRef.current) fileInputRef.current.value = "";
            }}
          />
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => fileInputRef.current?.click()}
          >
            Add Files
          </button>
          <span className="muted" style={{ marginLeft: 8, fontSize: "0.85em" }}>
            or paste images (Ctrl+V / ⌘V)
          </span>
          {attachments.length > 0 && (
            <ul style={{ marginTop: 8, paddingLeft: 0, listStyle: "none" }}>
              {attachments.map((f, i) => {
                const previewUrl = previewUrls[i];
                const isImage = !!previewUrl;
                const isHovered = hoveredPreview === i;
                return (
                  <li key={`${f.name}-${f.size}-${f.lastModified}`} style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 8 }}>
                    {isImage && previewUrl && (
                      <img
                        src={previewUrl}
                        alt={f.name}
                        onMouseEnter={() => setHoveredPreview(i)}
                        onMouseLeave={() => setHoveredPreview(null)}
                        style={{
                          height: isHovered ? 240 : 48,
                          maxWidth: isHovered ? 480 : 48,
                          objectFit: "contain",
                          borderRadius: 4,
                          border: "1px solid var(--border)",
                          cursor: "zoom-in",
                          transition: "height 0.2s ease, max-width 0.2s ease",
                          zIndex: isHovered ? 10 : 1,
                          position: isHovered ? "relative" : "static",
                        }}
                      />
                    )}
                    <span>{f.name} ({(f.size / 1024).toFixed(1)} KB)</span>
                    <button
                      type="button"
                      className="btn btn-secondary"
                      style={{ padding: "2px 8px", fontSize: "0.8em" }}
                      onClick={() => setAttachments((prev) => prev.filter((_, idx) => idx !== i))}
                    >
                      ✕
                    </button>
                  </li>
                );
              })}
            </ul>
          )}
        </div>

        <div className="form-actions">
          <button type="submit" className="btn btn-primary" disabled={loading}>
            {loading ? "Submitting..." : "Submit Request"}
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => navigate("/")}
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
