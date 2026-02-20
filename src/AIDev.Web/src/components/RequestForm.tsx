import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import {
  createRequest,
  type CreateRequest,
  type RequestType,
  type Priority,
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

  const [form, setForm] = useState<CreateRequest>({
    title: "",
    description: "",
    requestType: "Bug",
    priority: "Medium",
    stepsToReproduce: "",
    expectedBehavior: "",
    actualBehavior: "",
  });

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
    <div className="animate-[fadeIn_0.2s_ease-in]">
      <h1 className="text-2xl font-bold text-slate-800 mb-6">Submit New Request</h1>

      {error && (
        <div className="bg-red-50 text-red-800 px-4 py-3 rounded-lg mb-4 border border-red-200">
          {error}
        </div>
      )}

      <form onSubmit={handleSubmit} className="bg-white p-8 rounded-lg shadow-sm max-w-3xl">
        <div className="mb-5">
          <label htmlFor="title" className="block font-semibold mb-1.5 text-sm text-slate-800">
            Title *
          </label>
          <input
            id="title"
            type="text"
            required
            maxLength={200}
            value={form.title}
            onChange={(e) => setForm({ ...form, title: e.target.value })}
            placeholder="Short summary of the issue or request"
            className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div className="mb-5">
            <label htmlFor="requestType" className="block font-semibold mb-1.5 text-sm text-slate-800">
              Type
            </label>
            <select
              id="requestType"
              value={form.requestType}
              onChange={(e) =>
                setForm({
                  ...form,
                  requestType: e.target.value as RequestType,
                })
              }
              className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
            >
              {requestTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </div>

          <div className="mb-5">
            <label htmlFor="priority" className="block font-semibold mb-1.5 text-sm text-slate-800">
              Priority
            </label>
            <select
              id="priority"
              value={form.priority}
              onChange={(e) =>
                setForm({ ...form, priority: e.target.value as Priority })
              }
              className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
            >
              {priorities.map((p) => (
                <option key={p} value={p}>
                  {p}
                </option>
              ))}
            </select>
          </div>
        </div>

        <div className="mb-5">
          <label htmlFor="description" className="block font-semibold mb-1.5 text-sm text-slate-800">
            Description *
          </label>
          <textarea
            id="description"
            required
            rows={5}
            value={form.description}
            onChange={(e) => setForm({ ...form, description: e.target.value })}
            placeholder="Detailed explanation of the issue or feature request"
            className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
          />
        </div>

        {form.requestType === "Bug" && (
          <>
            <div className="mb-5">
              <label htmlFor="stepsToReproduce" className="block font-semibold mb-1.5 text-sm text-slate-800">
                Steps to Reproduce
              </label>
              <textarea
                id="stepsToReproduce"
                rows={4}
                value={form.stepsToReproduce}
                onChange={(e) =>
                  setForm({ ...form, stepsToReproduce: e.target.value })
                }
                placeholder="1. Go to...&#10;2. Click on...&#10;3. Observe..."
                className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
              />
            </div>

            <div className="mb-5">
              <label htmlFor="expectedBehavior" className="block font-semibold mb-1.5 text-sm text-slate-800">
                Expected Behavior
              </label>
              <textarea
                id="expectedBehavior"
                rows={3}
                value={form.expectedBehavior}
                onChange={(e) =>
                  setForm({ ...form, expectedBehavior: e.target.value })
                }
                placeholder="What should happen?"
                className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
              />
            </div>

            <div className="mb-5">
              <label htmlFor="actualBehavior" className="block font-semibold mb-1.5 text-sm text-slate-800">
                Actual Behavior
              </label>
              <textarea
                id="actualBehavior"
                rows={3}
                value={form.actualBehavior}
                onChange={(e) =>
                  setForm({ ...form, actualBehavior: e.target.value })
                }
                placeholder="What actually happens?"
                className="w-full px-3 py-2.5 border border-slate-200 rounded-lg text-sm font-[inherit] transition-colors duration-150 focus:outline-none focus:border-primary focus:ring-3 focus:ring-blue-500/10"
              />
            </div>
          </>
        )}

        <div className="flex gap-3 mt-6">
          <button
            type="submit"
            className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-primary text-white hover:bg-primary-hover cursor-pointer transition-all duration-150 disabled:opacity-60 disabled:cursor-not-allowed"
            disabled={loading}
          >
            {loading ? "Submitting..." : "Submit Request"}
          </button>
          <button
            type="button"
            className="inline-flex items-center justify-center px-5 py-2 text-sm font-medium rounded-lg bg-slate-100 text-slate-800 border border-slate-200 hover:bg-slate-200 cursor-pointer transition-all duration-150"
            onClick={() => navigate("/")}
          >
            Cancel
          </button>
        </div>
      </form>
    </div>
  );
}
