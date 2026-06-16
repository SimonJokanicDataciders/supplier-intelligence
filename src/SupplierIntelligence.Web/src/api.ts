export type RiskLevel = 'Unknown' | 'Low' | 'Medium' | 'High'

export type SupplierSummary = {
  id: number
  name: string
  countryCode: string
  industry: string
  websiteUrl: string | null
  registryNumber: string | null
  vatNumber: string | null
  certificationHints: string | null
  riskLevel: RiskLevel
  isArchived: boolean
  certificationCount: number
  sourceCheckCount: number
  riskAssessmentCount: number
}

export type Certification = {
  id: number
  standard: string
  certificateNumber: string
  issuer: string
  validUntil: string | null
  isVerified: boolean
  verificationNotes: string
  createdAt: string
}

export type SourceCheck = {
  id: number
  sourceName: string
  url: string
  status: string
  notes: string
  checkedAt: string
}

export type ResearchSource = {
  id: number
  sourceCheckId: number | null
  sourceName: string
  url: string
  kind: string
  status: string
  relevance: string
  summary: string
  createdAt: string
}

export type SupplierFact = {
  id: number
  researchSourceId: number | null
  factType: string
  value: string
  evidenceText: string
  sourceName: string
  sourceUrl: string
  confidence: string
  createdAt: string
}

export type RiskAssessment = {
  id: number
  riskLevel: RiskLevel
  score: number
  focus: string
  summaryMarkdown: string
  provider: string
  model: string
  promptFocus: string | null
  evidenceSnapshotJson: string | null
  generationDurationMs: number | null
  createdAt: string
}

export type AnalysisJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed'

export type AnalysisJob = {
  id: number
  jobType: string
  status: AnalysisJobStatus
  progressMessage: string
  errorMessage: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}

export type SupplierDetail = SupplierSummary & {
  createdAt: string
  analysisJobs: AnalysisJob[]
  certifications: Certification[]
  sourceChecks: SourceCheck[]
  researchSources: ResearchSource[]
  supplierFacts: SupplierFact[]
  riskAssessments: RiskAssessment[]
}

export type LocalModelInfo = {
  name: string
  sizeBytes: number
  modifiedAt: string | null
}

export type LocalModelStatus = {
  provider: string
  baseUrl: string
  defaultModel: string
  isReachable: boolean
  errorMessage: string | null
  models: LocalModelInfo[]
}

export type CreateSupplierInput = {
  name: string
  countryCode: string
  industry: string
  websiteUrl: string | null
  registryNumber: string | null
  vatNumber: string | null
  certificationHints: string | null
  runInitialAnalysis: boolean
}

export type RiskAssessmentInput = {
  riskLevel: RiskLevel
  score: number
  focus: string
  summaryMarkdown: string
}

export type SourceCheckInput = {
  sourceName: string
  url: string
  status: 'NotChecked' | 'Reachable' | 'Blocked' | 'Failed'
  notes: string
}

export type CheckSourceEvidenceInput = {
  sourceName: string
  url: string
}

export type CertificationInput = {
  standard: string
  certificateNumber: string
  issuer: string
  validUntil: string | null
  isVerified: boolean
}

export type VerifyCertificationInput = {
  standard: string
  certificateNumber: string
  issuer: string
  validUntil: string | null
}

export type GenerateRiskAssessmentInput = {
  focus: string
  model: string | null
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, {
    headers: {
      'Content-Type': 'application/json',
      ...init?.headers,
    },
    ...init,
  })

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`

    try {
      const body = (await response.json()) as { error?: string; detail?: string; title?: string }
      message = body.error ?? body.detail ?? body.title ?? message
    } catch {}

    throw new Error(message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return response.json() as Promise<T>
}

export function getSuppliers() {
  return request<SupplierSummary[]>('/api/suppliers')
}

export function getSupplier(id: number) {
  return request<SupplierDetail>(`/api/suppliers/${id}`)
}

export function getLocalModelStatus() {
  return request<LocalModelStatus>('/api/local-models/')
}

export function createSupplier(input: CreateSupplierInput) {
  return request<SupplierDetail>('/api/suppliers', {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function archiveSupplier(id: number) {
  return request<void>(`/api/suppliers/${id}/archive`, {
    method: 'POST',
  })
}

export function createRiskAssessment(supplierId: number, input: RiskAssessmentInput) {
  return request<RiskAssessment>(`/api/suppliers/${supplierId}/risk-assessments`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function addSourceCheck(supplierId: number, input: SourceCheckInput) {
  return request<SourceCheck>(`/api/suppliers/${supplierId}/source-checks`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function checkSourceEvidence(supplierId: number, input: CheckSourceEvidenceInput) {
  return request<SourceCheck>(`/api/suppliers/${supplierId}/source-checks/check`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function addCertification(supplierId: number, input: CertificationInput) {
  return request<Certification>(`/api/suppliers/${supplierId}/certifications`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function verifyCertification(supplierId: number, input: VerifyCertificationInput) {
  return request<Certification>(`/api/suppliers/${supplierId}/certifications/verify`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function discoverCertificationsFromWebsite(supplierId: number) {
  return request<Certification[]>(`/api/suppliers/${supplierId}/certifications/discover-from-website`, {
    method: 'POST',
  })
}

export function updateRiskAssessment(
  supplierId: number,
  assessmentId: number,
  input: RiskAssessmentInput,
) {
  return request<RiskAssessment>(`/api/suppliers/${supplierId}/risk-assessments/${assessmentId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function deleteRiskAssessment(supplierId: number, assessmentId: number) {
  return request<void>(`/api/suppliers/${supplierId}/risk-assessments/${assessmentId}`, {
    method: 'DELETE',
  })
}

export function generateRiskAssessment(
  supplierId: number,
  input: GenerateRiskAssessmentInput,
  signal?: AbortSignal,
) {
  return request<{ supplierId: number; supplierName: string; riskAssessment: RiskAssessment }>(
    `/api/suppliers/${supplierId}/risk-assessments/generate`,
    {
      method: 'POST',
      body: JSON.stringify(input),
      signal,
    },
  )
}
