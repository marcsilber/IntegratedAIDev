import { useState, useEffect, type FormEvent } from "react";
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
      const created = await createRequest(form);
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
    <div className="page">
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
