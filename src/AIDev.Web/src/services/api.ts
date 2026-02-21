import axios from "axios";
import { PublicClientApplication } from "@azure/msal-browser";
import { msalConfig, apiScopes } from "../auth/authConfig";

const API_BASE_URL = import.meta.env.VITE_API_URL || "https://localhost:7251";

// Shared MSAL instance — same one used by MsalProvider in main.tsx
export const msalInstance = new PublicClientApplication(msalConfig);

const api = axios.create({
  baseURL: `${API_BASE_URL}/api`,
  headers: {
    "Content-Type": "application/json",
  },
});

// Request interceptor to add auth token
api.interceptors.request.use(async (config) => {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length > 0) {
    try {
      const response = await msalInstance.acquireTokenSilent({
        ...apiScopes,
        account: accounts[0],
      });
      config.headers.Authorization = `Bearer ${response.accessToken}`;
    } catch (err) {
      console.error("Failed to acquire token silently:", err);
      // Fallback to interactive redirect
      await msalInstance.acquireTokenRedirect(apiScopes);
      // Page will reload after redirect — no token to set here
      return config;
    }
  }
  return config;
});

// ── Types ─────────────────────────────────────────────────────────────────

export type RequestType = "Bug" | "Feature" | "Enhancement" | "Question";
export type Priority = "Low" | "Medium" | "High" | "Critical";
export type RequestStatus =
  | "New"
  | "NeedsClarification"
  | "Triaged"
  | "ArchitectReview"
  | "Approved"
  | "InProgress"
  | "Done"
  | "Rejected";

export type AgentDecision = "Approve" | "Reject" | "Clarify";
export type ArchitectDecision = "Pending" | "Approved" | "Rejected" | "Revised";
export type CopilotImplementationStatus = "Pending" | "Working" | "PrOpened" | "PrMerged" | "Failed";

export interface DevRequest {
  id: number;
  title: string;
  description: string;
  requestType: RequestType;
  priority: Priority;
  stepsToReproduce?: string;
  expectedBehavior?: string;
  actualBehavior?: string;
  status: RequestStatus;
  submittedBy: string;
  submittedByEmail: string;
  projectId: number;
  projectName: string;
  gitHubIssueNumber?: number;
  gitHubIssueUrl?: string;
  createdAt: string;
  updatedAt: string;
  comments: Comment[];
  attachments: Attachment[];
  latestAgentReview?: AgentReview;
  agentReviewCount: number;
}

export interface AgentReview {
  id: number;
  devRequestId: number;
  requestTitle: string;
  agentType: string;
  decision: AgentDecision;
  reasoning: string;
  alignmentScore: number;
  completenessScore: number;
  salesAlignmentScore: number;
  suggestedPriority?: string;
  tags?: string;
  promptTokens: number;
  completionTokens: number;
  modelUsed: string;
  durationMs: number;
  createdAt: string;
}

export interface AgentStats {
  totalReviews: number;
  byDecision: Record<string, number>;
  averageAlignmentScore: number;
  averageCompletenessScore: number;
  averageSalesAlignmentScore: number;
  totalTokensUsed: number;
  averageDurationMs: number;
}

export interface AgentConfig {
  enabled: boolean;
  pollingIntervalSeconds: number;
  maxReviewsPerRequest: number;
  temperature: number;
  modelName: string;
  dailyTokenBudget: number;
  monthlyTokenBudget: number;
}

export interface AgentConfigUpdate {
  enabled?: boolean;
  pollingIntervalSeconds?: number;
  maxReviewsPerRequest?: number;
  temperature?: number;
  dailyTokenBudget?: number;
  monthlyTokenBudget?: number;
}

export interface TokenBudget {
  dailyTokensUsed: number;
  dailyTokenBudget: number;
  dailyBudgetExceeded: boolean;
  dailyReviewCount: number;
  monthlyTokensUsed: number;
  monthlyTokenBudget: number;
  monthlyBudgetExceeded: boolean;
  monthlyReviewCount: number;
}

export interface Attachment {
  id: number;
  fileName: string;
  contentType: string;
  fileSizeBytes: number;
  uploadedBy: string;
  createdAt: string;
  downloadUrl: string;
}

export interface Project {
  id: number;
  gitHubOwner: string;
  gitHubRepo: string;
  displayName: string;
  description?: string;
  fullName: string;
  isActive: boolean;
  lastSyncedAt: string;
  requestCount: number;
}

export interface UpdateProject {
  displayName?: string;
  description?: string;
  isActive?: boolean;
}

export interface Comment {
  id: number;
  author: string;
  content: string;
  isAgentComment: boolean;
  agentReviewId?: number;
  createdAt: string;
}

export interface CreateRequest {
  projectId: number;
  title: string;
  description: string;
  requestType: RequestType;
  priority: Priority;
  stepsToReproduce?: string;
  expectedBehavior?: string;
  actualBehavior?: string;
}

export interface UpdateRequest {
  title?: string;
  description?: string;
  requestType?: RequestType;
  priority?: Priority;
  stepsToReproduce?: string;
  expectedBehavior?: string;
  actualBehavior?: string;
  status?: RequestStatus;
}

export interface Dashboard {
  totalRequests: number;
  byStatus: Record<string, number>;
  byType: Record<string, number>;
  byPriority: Record<string, number>;
  recentRequests: DevRequest[];
}

// ── API Functions ─────────────────────────────────────────────────────────

export async function getRequests(params?: {
  status?: RequestStatus;
  type?: RequestType;
  priority?: Priority;
  search?: string;
}): Promise<DevRequest[]> {
  const { data } = await api.get("/requests", { params });
  return data;
}

export async function getRequest(id: number): Promise<DevRequest> {
  const { data } = await api.get(`/requests/${id}`);
  return data;
}

export async function createRequest(
  request: CreateRequest,
  files?: File[]
): Promise<DevRequest> {
  const formData = new FormData();
  formData.append("projectId", String(request.projectId));
  formData.append("title", request.title);
  formData.append("description", request.description);
  formData.append("requestType", request.requestType);
  formData.append("priority", request.priority);
  if (request.stepsToReproduce) formData.append("stepsToReproduce", request.stepsToReproduce);
  if (request.expectedBehavior) formData.append("expectedBehavior", request.expectedBehavior);
  if (request.actualBehavior) formData.append("actualBehavior", request.actualBehavior);
  if (files) files.forEach((f) => formData.append("files", f));
  const { data } = await api.post("/requests", formData, {
    headers: { "Content-Type": "multipart/form-data" },
  });
  return data;
}

export async function updateRequest(
  id: number,
  request: UpdateRequest
): Promise<DevRequest> {
  const { data } = await api.put(`/requests/${id}`, request);
  return data;
}

export async function deleteRequest(id: number): Promise<void> {
  await api.delete(`/requests/${id}`);
}

export async function addComment(
  requestId: number,
  content: string
): Promise<Comment> {
  const { data } = await api.post(`/requests/${requestId}/comments`, {
    content,
  });
  return data;
}

export async function getDashboard(): Promise<Dashboard> {
  const { data } = await api.get("/dashboard");
  return data;
}

// ── Project / Admin Functions ─────────────────────────────────────────────

export async function getProjects(): Promise<Project[]> {
  const { data } = await api.get("/projects");
  return data;
}

export async function getAdminProjects(): Promise<Project[]> {
  const { data } = await api.get("/admin/projects");
  return data;
}

export async function syncProjects(): Promise<Project[]> {
  const { data } = await api.post("/admin/projects/sync");
  return data;
}

export async function updateProject(
  id: number,
  update: UpdateProject
): Promise<Project> {
  const { data } = await api.put(`/admin/projects/${id}`, update);
  return data;
}

// ── Attachment Functions ──────────────────────────────────────────────────

export async function uploadAttachments(
  requestId: number,
  files: File[]
): Promise<Attachment[]> {
  const formData = new FormData();
  files.forEach((f) => formData.append("files", f));
  const { data } = await api.post(
    `/requests/${requestId}/attachments`,
    formData,
    { headers: { "Content-Type": "multipart/form-data" } }
  );
  return data;
}

export function getAttachmentUrl(requestId: number, attachmentId: number): string {
  return `${API_BASE_URL}/api/requests/${requestId}/attachments/${attachmentId}`;
}

export async function fetchAttachmentBlob(
  requestId: number,
  attachmentId: number
): Promise<string> {
  const response = await api.get(
    `/requests/${requestId}/attachments/${attachmentId}`,
    { responseType: "blob" }
  );
  return URL.createObjectURL(response.data);
}

export async function downloadAttachment(
  requestId: number,
  attachmentId: number,
  fileName: string
): Promise<void> {
  const blobUrl = await fetchAttachmentBlob(requestId, attachmentId);
  const a = document.createElement("a");
  a.href = blobUrl;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(blobUrl);
}

export async function deleteAttachment(
  requestId: number,
  attachmentId: number
): Promise<void> {
  await api.delete(`/requests/${requestId}/attachments/${attachmentId}`);
}

// ── Agent Functions ───────────────────────────────────────────────────────

export async function getAgentReviews(params?: {
  requestId?: number;
  decision?: AgentDecision;
}): Promise<AgentReview[]> {
  const { data } = await api.get("/agent/reviews", { params });
  return data;
}

export async function getAgentReview(id: number): Promise<AgentReview> {
  const { data } = await api.get(`/agent/reviews/${id}`);
  return data;
}

export async function overrideAgentReview(
  reviewId: number,
  newStatus: RequestStatus,
  reason?: string
): Promise<AgentReview> {
  const { data } = await api.post(`/agent/reviews/${reviewId}/override`, {
    newStatus,
    reason,
  });
  return data;
}

export async function getAgentStats(): Promise<AgentStats> {
  const { data } = await api.get("/agent/stats");
  return data;
}

export async function getAgentConfig(): Promise<AgentConfig> {
  const { data } = await api.get("/agent/config");
  return data;
}

export async function updateAgentConfig(
  config: AgentConfigUpdate
): Promise<AgentConfig> {
  const { data } = await api.put("/agent/config", config);
  return data;
}

export async function triggerReReview(requestId: number): Promise<void> {
  await api.post(`/agent/reviews/re-review/${requestId}`);
}

export async function getAgentBudget(): Promise<TokenBudget> {
  const { data } = await api.get("/agent/budget");
  return data;
}

// ── Architect Types ───────────────────────────────────────────────────────

export interface ArchitectReviewResponse {
  id: number;
  devRequestId: number;
  requestTitle: string;
  solutionSummary: string;
  approach: string;
  impactedFiles: ImpactedFile[];
  newFiles: NewFile[];
  dataMigration: DataMigrationInfo;
  breakingChanges: string[];
  dependencyChanges: DependencyChange[];
  risks: RiskInfo[];
  estimatedComplexity: string;
  estimatedEffort: string;
  implementationOrder: string[];
  testingNotes: string;
  architecturalNotes: string;
  decision: ArchitectDecision;
  humanFeedback?: string;
  approvedBy?: string;
  approvedAt?: string;
  filesAnalysed: number;
  totalTokensUsed: number;
  modelUsed: string;
  totalDurationMs: number;
  createdAt: string;
}

export interface ImpactedFile {
  path: string;
  action: string;
  description: string;
  estimatedLinesChanged: number;
}

export interface NewFile {
  path: string;
  description: string;
  estimatedLines: number;
}

export interface DataMigrationInfo {
  required: boolean;
  description?: string;
  steps: string[];
}

export interface DependencyChange {
  package: string;
  action: string;
  version: string;
  reason: string;
}

export interface RiskInfo {
  description: string;
  severity: string;
  mitigation: string;
}

export interface ArchitectConfig {
  enabled: boolean;
  pollingIntervalSeconds: number;
  maxReviewsPerRequest: number;
  maxFilesToRead: number;
  temperature: number;
  modelName: string;
  dailyTokenBudget: number;
  monthlyTokenBudget: number;
}

export interface ArchitectConfigUpdate {
  enabled?: boolean;
  pollingIntervalSeconds?: number;
  maxReviewsPerRequest?: number;
  maxFilesToRead?: number;
  temperature?: number;
  dailyTokenBudget?: number;
  monthlyTokenBudget?: number;
}

export interface ArchitectStats {
  totalAnalyses: number;
  pendingReview: number;
  approved: number;
  rejected: number;
  revised: number;
  averageFilesAnalysed: number;
  totalTokensUsed: number;
  averageDurationMs: number;
}

// ── Architect Functions ───────────────────────────────────────────────────

export async function getArchitectReviews(params?: {
  requestId?: number;
  decision?: ArchitectDecision;
}): Promise<ArchitectReviewResponse[]> {
  const { data } = await api.get("/architect/reviews", { params });
  return data;
}

export async function getArchitectReview(
  id: number
): Promise<ArchitectReviewResponse> {
  const { data } = await api.get(`/architect/reviews/${id}`);
  return data;
}

export async function approveArchitectReview(
  id: number,
  reason?: string
): Promise<ArchitectReviewResponse> {
  const { data } = await api.post(`/architect/reviews/${id}/approve`, {
    reason,
  });
  return data;
}

export async function rejectArchitectReview(
  id: number,
  reason: string
): Promise<ArchitectReviewResponse> {
  const { data } = await api.post(`/architect/reviews/${id}/reject`, {
    reason,
  });
  return data;
}

export async function postArchitectFeedback(
  id: number,
  feedback: string
): Promise<void> {
  await api.post(`/architect/reviews/${id}/feedback`, { feedback });
}

export async function triggerArchitectReAnalysis(
  requestId: number
): Promise<void> {
  await api.post(`/architect/reviews/re-analyse/${requestId}`);
}

export async function getArchitectConfig(): Promise<ArchitectConfig> {
  const { data } = await api.get("/architect/config");
  return data;
}

export async function updateArchitectConfig(
  config: ArchitectConfigUpdate
): Promise<ArchitectConfig> {
  const { data } = await api.put("/architect/config", config);
  return data;
}

export async function getArchitectBudget(): Promise<TokenBudget> {
  const { data } = await api.get("/architect/budget");
  return data;
}

export async function getArchitectStats(): Promise<ArchitectStats> {
  const { data } = await api.get("/architect/stats");
  return data;
}

// ── Implementation / Copilot Types ────────────────────────────────────────

export interface ImplementationStatus {
  requestId: number;
  title: string;
  issueNumber?: number;
  copilotStatus?: CopilotImplementationStatus;
  copilotSessionId?: string;
  prNumber?: number;
  prUrl?: string;
  triggeredAt?: string;
  completedAt?: string;
  elapsedMinutes?: number;
}

export interface ImplementationTrigger {
  additionalInstructions?: string;
  model?: string;
  baseBranch?: string;
}

export interface ImplementationTriggerResponse {
  requestId: number;
  issueNumber?: number;
  copilotStatus: CopilotImplementationStatus;
  triggeredAt: string;
}

export interface ImplementationConfig {
  enabled: boolean;
  autoTriggerOnApproval: boolean;
  pollingIntervalSeconds: number;
  prPollIntervalSeconds: number;
  maxConcurrentSessions: number;
  baseBranch: string;
  model: string;
  customAgent: string;
  maxRetries: number;
}

export interface ImplementationConfigUpdate {
  enabled?: boolean;
  autoTriggerOnApproval?: boolean;
  pollingIntervalSeconds?: number;
  prPollIntervalSeconds?: number;
  maxConcurrentSessions?: number;
  baseBranch?: string;
  model?: string;
  maxRetries?: number;
}

export interface ImplementationStats {
  totalTriggered: number;
  pending: number;
  working: number;
  prOpened: number;
  prMerged: number;
  failed: number;
  successRate: number;
  averageCompletionMinutes: number;
  activeSessions: number;
}

// ── Implementation / Copilot Functions ────────────────────────────────────

export async function triggerImplementation(
  requestId: number,
  trigger?: ImplementationTrigger
): Promise<ImplementationTriggerResponse> {
  const { data } = await api.post(`/implementation/trigger/${requestId}`, trigger || {});
  return data;
}

export async function reTriggerImplementation(
  requestId: number
): Promise<ImplementationTriggerResponse> {
  const { data } = await api.post(`/implementation/re-trigger/${requestId}`);
  return data;
}

export async function rejectImplementation(
  requestId: number,
  reason?: string
): Promise<{ message: string; reason: string }> {
  const { data } = await api.post(`/implementation/reject/${requestId}`, { reason });
  return data;
}

export async function getImplementationStatus(
  requestId: number
): Promise<ImplementationStatus> {
  const { data } = await api.get(`/implementation/status/${requestId}`);
  return data;
}

export async function getImplementationSessions(
  status?: CopilotImplementationStatus
): Promise<ImplementationStatus[]> {
  const { data } = await api.get("/implementation/sessions", {
    params: status ? { status } : undefined,
  });
  return data;
}

export async function getImplementationConfig(): Promise<ImplementationConfig> {
  const { data } = await api.get("/implementation/config");
  return data;
}

export async function updateImplementationConfig(
  config: ImplementationConfigUpdate
): Promise<ImplementationConfig> {
  const { data } = await api.put("/implementation/config", config);
  return data;
}

export async function getImplementationStats(): Promise<ImplementationStats> {
  const { data } = await api.get("/implementation/stats");
  return data;
}

// ── Pipeline Orchestrator Types ───────────────────────────────────────────

export type DeploymentStatus = "None" | "Pending" | "InProgress" | "Succeeded" | "Failed";
export type DeploymentMode = "Auto" | "Staged";
export type StallSeverity = "Warning" | "Critical";

export interface PipelineHealth {
  totalStalled: number;
  stalledNeedsClarification: number;
  stalledArchitectReview: number;
  stalledApproved: number;
  stalledFailed: number;
  deploymentsPending: number;
  deploymentsInProgress: number;
  deploymentsSucceeded: number;
  deploymentsFailed: number;
  deploymentsRetrying: number;
  stagedForDeploy: number;
  branchesDeleted: number;
  branchesOutstanding: number;
}

export interface StalledRequest {
  requestId: number;
  title: string;
  status: string;
  stallReason: string;
  severity: StallSeverity;
  gitHubIssueNumber?: number;
  daysStalled: number;
  stallNotifiedAt?: string;
}

export interface DeploymentTracking {
  requestId: number;
  title: string;
  prNumber?: number;
  deploymentStatus: DeploymentStatus;
  deploymentRunId?: number;
  mergedAt?: string;
  deployedAt?: string;
  branchDeleted: boolean;
  branchName?: string;
  retryCount: number;
}

export interface StagedDeployment {
  requestId: number;
  title: string;
  prNumber: number;
  prUrl: string;
  branchName: string;
  qualityScore: number;
  approvedAt?: string;
  gitHubIssueNumber?: number;
}

export interface DeployTriggerResponse {
  mergedPrs: number[];
  failedPrs: number[];
  message: string;
}

export interface WorkflowRunInfo {
  runId: number;
  status: string;
  conclusion: string;
  createdAt: string;
}

export interface DeployStatus {
  deploymentMode: DeploymentMode;
  api: WorkflowRunInfo[];
  web: WorkflowRunInfo[];
}

export interface PipelineConfig {
  enabled: boolean;
  pollIntervalSeconds: number;
  needsClarificationStaleDays: number;
  architectReviewStaleDays: number;
  approvedStaleDays: number;
  failedStaleHours: number;
  deploymentMode: string;
  maxDeployRetries: number;
}

export interface PipelineConfigUpdate {
  enabled?: boolean;
  pollIntervalSeconds?: number;
  needsClarificationStaleDays?: number;
  architectReviewStaleDays?: number;
  approvedStaleDays?: number;
  failedStaleHours?: number;
  deploymentMode?: string;
  maxDeployRetries?: number;
}

// ── Pipeline Orchestrator Functions ───────────────────────────────────────

export async function getPipelineHealth(): Promise<PipelineHealth> {
  const { data } = await api.get("/orchestrator/health");
  return data;
}

export async function getStalledRequests(): Promise<StalledRequest[]> {
  const { data } = await api.get("/orchestrator/stalled");
  return data;
}

export async function getDeployments(status?: DeploymentStatus): Promise<DeploymentTracking[]> {
  const { data } = await api.get("/orchestrator/deployments", {
    params: status ? { status } : undefined,
  });
  return data;
}

export async function getPipelineConfig(): Promise<PipelineConfig> {
  const { data } = await api.get("/orchestrator/config");
  return data;
}

export async function updatePipelineConfig(
  config: PipelineConfigUpdate
): Promise<PipelineConfig> {
  const { data } = await api.put("/orchestrator/config", config);
  return data;
}

export async function getStagedDeployments(): Promise<StagedDeployment[]> {
  const { data } = await api.get("/orchestrator/staged");
  return data;
}

export async function triggerDeploy(): Promise<DeployTriggerResponse> {
  const { data } = await api.post("/orchestrator/deploy");
  return data;
}

export async function triggerWorkflows(): Promise<{ apiTriggered: boolean; webTriggered: boolean; message: string }> {
  const { data } = await api.post("/orchestrator/deploy/trigger-workflows");
  return data;
}

export async function retryDeployment(requestId: number): Promise<{ success: boolean; message: string }> {
  const { data } = await api.post(`/orchestrator/deploy/retry/${requestId}`);
  return data;
}

export async function getDeployStatus(): Promise<DeployStatus> {
  const { data } = await api.get("/orchestrator/deploy/status");
  return data;
}

export default api;
