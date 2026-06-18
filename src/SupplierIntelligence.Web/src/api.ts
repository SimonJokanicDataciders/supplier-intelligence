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

export type SupplierMatchCandidateStatus = 'Proposed' | 'Confirmed' | 'Rejected'

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

export type SupplierMatchCandidate = {
  id: number
  candidateName: string
  countryCode: string | null
  websiteUrl: string | null
  sourceName: string | null
  sourceUrl: string | null
  confidenceScore: number
  matchReason: string
  status: SupplierMatchCandidateStatus
  createdAt: string
  reviewedAt: string | null
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

export type SupplierReviewSummary = {
  supplierId: number
  supplierName: string
  headline: string
  nextAction: SupplierReviewNextAction
  knownInformation: string[]
  missingInformation: string[]
  trustSignals: SupplierTrustSignals
}

export type SupplierReviewNextAction = {
  type: string
  title: string
  description: string
  buttonLabel: string
  step: ReviewStepName
}

export type ReviewStepName = 'briefing' | 'sources' | 'questions' | 'connections' | 'report'

export type SupplierTrustSignals = {
  identity: string
  evidence: string
  certifications: string
  risk: string
}

export type SupplierAnalytics = {
  supplierId: number
  supplierName: string
  overallTrustScore: number
  trustBreakdown: TrustBreakdownItem[]
  sourceMix: SourceMixItem[]
  timeline: TimelineItem[]
  strongestSignals: string[]
  weakestGaps: string[]
}

export type SupplierConnection = {
  supplierId: number
  supplierName: string
  countryCode: string
  industry: string
  websiteUrl: string | null
  strengthLabel: string
  reasons: string[]
  sharedTerms: string[]
}

export type OpenQuestionResolution = {
  question: string
  status: string
  evidenceNote: string
  sourceName: string
}

export type RecheckOpenQuestionsResponse = {
  resolved: OpenQuestionResolution[]
  unresolved: OpenQuestionResolution[]
}

export type TrustBreakdownItem = {
  label: string
  score: number
  status: string
  explanation: string
}

export type SourceMixItem = {
  label: string
  count: number
  status: string
}

export type TimelineItem = {
  occurredAt: string
  type: string
  title: string
  description: string
  status: string
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
  isApiKeyConfigured: boolean
  apiKeySource: string
  apiKeyFingerprint: string
  apiKeyUpdatedAt: string | null
  isReachable: boolean
  errorMessage: string | null
  models: LocalModelInfo[]
}

export type CreateSupplierInput = {
  name: string
  countryCode: string
  industry: string
  websiteUrl: string | null
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

export type ResearchWebsiteSourceInput = {
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

export function getSupplierReviewSummary(id: number) {
  return request<SupplierReviewSummary>(`/api/suppliers/${id}/review-summary`)
}

export function getSupplierAnalytics(id: number) {
  return request<SupplierAnalytics>(`/api/suppliers/${id}/analytics`)
}

export function getSupplierConnections(id: number) {
  return request<SupplierConnection[]>(`/api/suppliers/${id}/connections`)
}

export function recheckOpenQuestions(id: number, questions: string[]) {
  return request<RecheckOpenQuestionsResponse>(`/api/suppliers/${id}/open-questions/recheck`, {
    method: 'POST',
    body: JSON.stringify({ questions }),
  })
}

export function getSupplierAnalysisJobs(supplierId: number) {
  return request<AnalysisJob[]>(`/api/suppliers/${supplierId}/analysis-jobs`)
}

export function getSupplierAnalysisJob(supplierId: number, jobId: number) {
  return request<AnalysisJob>(`/api/suppliers/${supplierId}/analysis-jobs/${jobId}`)
}

export function queueSupplierAnalysis(supplierId: number) {
  return request<AnalysisJob>(`/api/suppliers/${supplierId}/analysis-jobs`, {
    method: 'POST',
  })
}

export function getSupplierMatchCandidates(supplierId: number) {
  return request<SupplierMatchCandidate[]>(`/api/suppliers/${supplierId}/match-candidates`)
}

export function getLocalModelStatus() {
  return request<LocalModelStatus>('/api/local-models/')
}

export function saveOpenRouterApiKey(apiKey: string) {
  return request<{ isApiKeyConfigured: boolean; apiKeySource: string }>('/api/local-models/openrouter-key', {
    method: 'POST',
    body: JSON.stringify({ apiKey }),
  })
}

export function clearOpenRouterApiKey() {
  return request<void>('/api/local-models/openrouter-key', {
    method: 'DELETE',
  })
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

export function updateSupplierIndustry(id: number, industry: string) {
  return request<SupplierDetail>(`/api/suppliers/${id}/industry`, {
    method: 'PATCH',
    body: JSON.stringify({ industry }),
  })
}

export function suggestSupplierMatchCandidates(supplierId: number) {
  return request<SupplierMatchCandidate[]>(`/api/suppliers/${supplierId}/match-candidates/suggest`, {
    method: 'POST',
  })
}

export function confirmSupplierMatchCandidate(supplierId: number, candidateId: number) {
  return request<SupplierMatchCandidate>(
    `/api/suppliers/${supplierId}/match-candidates/${candidateId}/confirm`,
    {
      method: 'POST',
    },
  )
}

export function rejectSupplierMatchCandidate(supplierId: number, candidateId: number) {
  return request<SupplierMatchCandidate>(
    `/api/suppliers/${supplierId}/match-candidates/${candidateId}/reject`,
    {
      method: 'POST',
    },
  )
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

export function updateSourceCheck(supplierId: number, sourceCheckId: number, input: SourceCheckInput) {
  return request<SourceCheck>(`/api/suppliers/${supplierId}/source-checks/${sourceCheckId}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  })
}

export function deleteSourceCheck(supplierId: number, sourceCheckId: number) {
  return request<void>(`/api/suppliers/${supplierId}/source-checks/${sourceCheckId}`, {
    method: 'DELETE',
  })
}

export function checkSourceEvidence(supplierId: number, input: CheckSourceEvidenceInput) {
  return request<SourceCheck>(`/api/suppliers/${supplierId}/source-checks/check`, {
    method: 'POST',
    body: JSON.stringify(input),
  })
}

export function researchWebsiteSource(supplierId: number, input: ResearchWebsiteSourceInput) {
  return request<SourceCheck[]>(`/api/suppliers/${supplierId}/source-checks/research-website`, {
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
