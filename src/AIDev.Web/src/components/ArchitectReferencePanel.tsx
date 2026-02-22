import { useEffect, useState } from "react";
import {
  getArchitectReference,
  type ArchitectReference,
  type TableSchema,
} from "../services/api";

type TabKey = "schema" | "architecture" | "decisions";

export default function ArchitectReferencePanel() {
  const [data, setData] = useState<ArchitectReference | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<TabKey>("schema");
  const [expandedTables, setExpandedTables] = useState<Set<string>>(new Set());

  useEffect(() => {
    loadData();
  }, []);

  async function loadData() {
    try {
      setLoading(true);
      const ref = await getArchitectReference();
      setData(ref);
    } catch {
      setError("Failed to load architect reference data");
    } finally {
      setLoading(false);
    }
  }

  function toggleTable(tableName: string) {
    setExpandedTables((prev) => {
      const next = new Set(prev);
      if (next.has(tableName)) next.delete(tableName);
      else next.add(tableName);
      return next;
    });
  }

  function expandAll() {
    if (!data) return;
    setExpandedTables(new Set(data.databaseSchema.map((t) => t.tableName)));
  }

  function collapseAll() {
    setExpandedTables(new Set());
  }

  if (loading) {
    return (
      <div style={{ padding: "2rem", textAlign: "center", color: "var(--text-muted)" }}>
        Loading architect reference data…
      </div>
    );
  }

  if (error) {
    return (
      <div style={{ padding: "2rem" }}>
        <div className="error-banner">{error}</div>
        <button className="btn btn-primary" onClick={loadData} style={{ marginTop: "1rem" }}>
          Retry
        </button>
      </div>
    );
  }

  if (!data) return null;

  const tabs: { key: TabKey; label: string }[] = [
    { key: "schema", label: "Database Schema" },
    { key: "architecture", label: "Architecture" },
    { key: "decisions", label: "Design Decisions" },
  ];

  return (
    <div style={{ padding: "1.5rem", maxWidth: "1200px", margin: "0 auto" }}>
      <h1 style={{ marginBottom: "0.5rem" }}>Architect Reference</h1>
      <p style={{ color: "var(--text-muted)", marginBottom: "1.5rem" }}>
        Centralized architectural information for human review and AI context.
      </p>

      {/* Tabs */}
      <div
        style={{
          display: "flex",
          gap: "0",
          borderBottom: "2px solid var(--border)",
          marginBottom: "1.5rem",
        }}
      >
        {tabs.map((tab) => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            style={{
              padding: "0.75rem 1.5rem",
              background: "none",
              border: "none",
              borderBottom: activeTab === tab.key ? "2px solid var(--primary)" : "2px solid transparent",
              color: activeTab === tab.key ? "var(--primary)" : "var(--text-muted)",
              fontWeight: activeTab === tab.key ? 600 : 400,
              cursor: "pointer",
              fontSize: "0.95rem",
              marginBottom: "-2px",
            }}
          >
            {tab.label}
          </button>
        ))}
      </div>

      {/* Tab Content */}
      {activeTab === "schema" && <SchemaTab tables={data.databaseSchema} expandedTables={expandedTables} onToggle={toggleTable} onExpandAll={expandAll} onCollapseAll={collapseAll} />}
      {activeTab === "architecture" && <ArchitectureTab overview={data.architectureOverview} />}
      {activeTab === "decisions" && <DecisionsTab decisions={data.designDecisions} />}
    </div>
  );
}

// ── Schema Tab ────────────────────────────────────────────────────────────

function SchemaTab({
  tables,
  expandedTables,
  onToggle,
  onExpandAll,
  onCollapseAll,
}: {
  tables: TableSchema[];
  expandedTables: Set<string>;
  onToggle: (name: string) => void;
  onExpandAll: () => void;
  onCollapseAll: () => void;
}) {
  return (
    <div>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "1rem" }}>
        <span style={{ color: "var(--text-muted)" }}>
          {tables.length} tables
        </span>
        <div style={{ display: "flex", gap: "0.5rem" }}>
          <button className="btn btn-secondary btn-sm" onClick={onExpandAll}>
            Expand All
          </button>
          <button className="btn btn-secondary btn-sm" onClick={onCollapseAll}>
            Collapse All
          </button>
        </div>
      </div>

      {tables.map((table) => {
        const isExpanded = expandedTables.has(table.tableName);
        const pkColumns = table.columns.filter((c) => c.isPrimaryKey);
        const fkColumns = table.columns.filter((c) => c.isForeignKey);

        return (
          <div
            key={table.tableName}
            style={{
              border: "1px solid var(--border)",
              borderRadius: "6px",
              marginBottom: "0.75rem",
              backgroundColor: "var(--surface)",
            }}
          >
            <div
              onClick={() => onToggle(table.tableName)}
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                padding: "0.75rem 1rem",
                cursor: "pointer",
                userSelect: "none",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: "0.75rem" }}>
                <span style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>
                  {isExpanded ? "▼" : "▶"}
                </span>
                <strong style={{ color: "var(--primary)" }}>{table.tableName}</strong>
                <span style={{ color: "var(--text-muted)", fontSize: "0.85rem" }}>
                  ({table.entityName})
                </span>
              </div>
              <div style={{ display: "flex", gap: "0.5rem" }}>
                <span
                  style={{
                    fontSize: "0.75rem",
                    padding: "0.15rem 0.5rem",
                    borderRadius: "4px",
                    backgroundColor: "#1e293b",
                    color: "var(--text-muted)",
                  }}
                >
                  {table.columns.length} cols
                </span>
                {pkColumns.length > 0 && (
                  <span
                    style={{
                      fontSize: "0.75rem",
                      padding: "0.15rem 0.5rem",
                      borderRadius: "4px",
                      backgroundColor: "#1e3a2f",
                      color: "var(--success)",
                    }}
                  >
                    PK: {pkColumns.map((c) => c.name).join(", ")}
                  </span>
                )}
                {fkColumns.length > 0 && (
                  <span
                    style={{
                      fontSize: "0.75rem",
                      padding: "0.15rem 0.5rem",
                      borderRadius: "4px",
                      backgroundColor: "#1e2a3b",
                      color: "#60a5fa",
                    }}
                  >
                    {fkColumns.length} FK{fkColumns.length > 1 ? "s" : ""}
                  </span>
                )}
              </div>
            </div>

            {isExpanded && (
              <div style={{ padding: "0 1rem 1rem 1rem" }}>
                <table
                  style={{
                    width: "100%",
                    borderCollapse: "collapse",
                    fontSize: "0.85rem",
                  }}
                >
                  <thead>
                    <tr
                      style={{
                        borderBottom: "1px solid var(--border)",
                        color: "var(--text-muted)",
                        textAlign: "left",
                      }}
                    >
                      <th style={{ padding: "0.4rem 0.5rem" }}>Column</th>
                      <th style={{ padding: "0.4rem 0.5rem" }}>Type</th>
                      <th style={{ padding: "0.4rem 0.5rem" }}>Nullable</th>
                      <th style={{ padding: "0.4rem 0.5rem" }}>Key</th>
                      <th style={{ padding: "0.4rem 0.5rem" }}>Max Length</th>
                    </tr>
                  </thead>
                  <tbody>
                    {table.columns.map((col) => (
                      <tr
                        key={col.name}
                        style={{
                          borderBottom: "1px solid var(--border)",
                        }}
                      >
                        <td
                          style={{
                            padding: "0.4rem 0.5rem",
                            fontFamily: "monospace",
                            color: col.isPrimaryKey ? "var(--success)" : col.isForeignKey ? "#60a5fa" : "var(--text)",
                          }}
                        >
                          {col.name}
                        </td>
                        <td
                          style={{
                            padding: "0.4rem 0.5rem",
                            fontFamily: "monospace",
                            color: "var(--text-muted)",
                          }}
                        >
                          {col.dataType}
                        </td>
                        <td style={{ padding: "0.4rem 0.5rem" }}>
                          {col.isNullable ? (
                            <span style={{ color: "var(--warning)" }}>Yes</span>
                          ) : (
                            <span style={{ color: "var(--text-muted)" }}>No</span>
                          )}
                        </td>
                        <td style={{ padding: "0.4rem 0.5rem" }}>
                          {col.isPrimaryKey && (
                            <span
                              style={{
                                color: "var(--success)",
                                fontWeight: 600,
                                marginRight: "0.25rem",
                              }}
                            >
                              PK
                            </span>
                          )}
                          {col.isForeignKey && (
                            <span style={{ color: "#60a5fa", fontWeight: 600 }}>FK</span>
                          )}
                        </td>
                        <td
                          style={{
                            padding: "0.4rem 0.5rem",
                            color: "var(--text-muted)",
                          }}
                        >
                          {col.maxLength ?? "—"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                {table.relationships.length > 0 && (
                  <div style={{ marginTop: "0.75rem" }}>
                    <strong style={{ fontSize: "0.85rem" }}>Relationships:</strong>
                    <ul style={{ margin: "0.25rem 0 0 1.25rem", fontSize: "0.85rem" }}>
                      {table.relationships.map((rel, i) => (
                        <li key={i} style={{ marginBottom: "0.25rem", color: "var(--text)" }}>
                          <span style={{ fontFamily: "monospace", color: "#60a5fa" }}>
                            {rel.fromColumns.join(", ")}
                          </span>
                          {" → "}
                          <span style={{ fontFamily: "monospace", color: "var(--primary)" }}>
                            {rel.toTable}
                          </span>
                          .
                          <span style={{ fontFamily: "monospace", color: "#60a5fa" }}>
                            {rel.toColumns.join(", ")}
                          </span>
                          <span style={{ color: "var(--text-muted)", marginLeft: "0.5rem" }}>
                            (ON DELETE {rel.deleteBehavior})
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

// ── Architecture Tab ──────────────────────────────────────────────────────

function ArchitectureTab({ overview }: { overview: ArchitectReference["architectureOverview"] }) {
  return (
    <div>
      <h2 style={{ marginBottom: "0.5rem" }}>{overview.systemName}</h2>
      <p style={{ color: "var(--text-muted)", marginBottom: "1.5rem" }}>
        {overview.description}
      </p>

      <h3 style={{ marginBottom: "1rem" }}>System Components</h3>
      <div style={{ display: "grid", gap: "1rem", gridTemplateColumns: "repeat(auto-fill, minmax(350px, 1fr))" }}>
        {overview.components.map((comp) => (
          <div
            key={comp.name}
            style={{
              border: "1px solid var(--border)",
              borderRadius: "6px",
              padding: "1rem",
              backgroundColor: "var(--surface)",
            }}
          >
            <h4 style={{ margin: "0 0 0.25rem 0", color: "var(--primary)" }}>
              {comp.name}
            </h4>
            <div
              style={{
                fontSize: "0.8rem",
                color: "var(--text-muted)",
                marginBottom: "0.5rem",
                fontFamily: "monospace",
              }}
            >
              {comp.technology}
            </div>
            <p style={{ fontSize: "0.9rem", margin: "0 0 0.75rem 0", color: "var(--text)" }}>
              {comp.description}
            </p>
            {comp.interactions.length > 0 && (
              <>
                <strong style={{ fontSize: "0.8rem", color: "var(--text-muted)" }}>
                  Interactions:
                </strong>
                <ul style={{ margin: "0.25rem 0 0 1.25rem", fontSize: "0.85rem" }}>
                  {comp.interactions.map((int_, i) => (
                    <li key={i} style={{ marginBottom: "0.15rem", color: "var(--text)" }}>
                      {int_}
                    </li>
                  ))}
                </ul>
              </>
            )}
          </div>
        ))}
      </div>

      <h3 style={{ margin: "1.5rem 0 1rem 0" }}>Data Flow</h3>
      <div
        style={{
          border: "1px solid var(--border)",
          borderRadius: "6px",
          padding: "1rem",
          backgroundColor: "var(--surface)",
        }}
      >
        <ol style={{ margin: 0, paddingLeft: "1.5rem" }}>
          {overview.dataFlow.map((step, i) => (
            <li
              key={i}
              style={{
                marginBottom: "0.5rem",
                color: "var(--text)",
                fontSize: "0.9rem",
              }}
            >
              {step.replace(/^\d+\.\s*/, "")}
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}

// ── Decisions Tab ─────────────────────────────────────────────────────────

function DecisionsTab({ decisions }: { decisions: ArchitectReference["designDecisions"] }) {
  return (
    <div>
      <h3 style={{ marginBottom: "1rem" }}>Key Design Decisions</h3>
      {decisions.map((decision, i) => (
        <div
          key={i}
          style={{
            border: "1px solid var(--border)",
            borderRadius: "6px",
            padding: "1rem",
            backgroundColor: "var(--surface)",
            marginBottom: "0.75rem",
          }}
        >
          <h4 style={{ margin: "0 0 0.5rem 0", color: "var(--primary)" }}>
            {decision.title}
          </h4>
          <div style={{ marginBottom: "0.5rem" }}>
            <strong style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
              Rationale:
            </strong>
            <p style={{ margin: "0.25rem 0 0 0", fontSize: "0.9rem", color: "var(--text)" }}>
              {decision.rationale}
            </p>
          </div>
          <div>
            <strong style={{ fontSize: "0.85rem", color: "var(--text-muted)" }}>
              Implications:
            </strong>
            <p style={{ margin: "0.25rem 0 0 0", fontSize: "0.9rem", color: "var(--text)" }}>
              {decision.implications}
            </p>
          </div>
        </div>
      ))}
    </div>
  );
}
