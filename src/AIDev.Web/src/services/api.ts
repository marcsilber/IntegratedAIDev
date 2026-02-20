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
  | "Triaged"
  | "Approved"
  | "InProgress"
  | "Done"
  | "Rejected";

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
  request: CreateRequest
): Promise<DevRequest> {
  const { data } = await api.post("/requests", request);
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

export default api;
