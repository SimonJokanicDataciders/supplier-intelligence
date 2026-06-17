import { StrictMode, useEffect, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import {
  archiveSupplier,
  createSupplier,
  getLocalModelStatus,
  getSupplier,
  getSuppliers,
  type AnalysisJob,
  type Certification,
  type CreateSupplierInput,
  type LocalModelStatus,
  type RiskAssessment,
  type RiskLevel,
  type ResearchSource,
  type SourceCheck,
  type SupplierFact,
  type SupplierDetail,
  type SupplierSummary,
} from './api.ts'
import './styles.css'

const emptySupplierForm: CreateSupplierInput = {
  name: '',
  countryCode: '',
  industry: '',
  websiteUrl: '',
  registryNumber: '',
  vatNumber: '',
  certificationHints: '',
  runInitialAnalysis: true,
}

function App() {
  const [suppliers, setSuppliers] = useState<SupplierSummary[]>([])
  const [selectedSupplierId, setSelectedSupplierId] = useState<number | null>(null)
  const [supplier, setSupplier] = useState<SupplierDetail | null>(null)
  const [supplierForm, setSupplierForm] = useState<CreateSupplierInput>(emptySupplierForm)
  const [localModelStatus, setLocalModelStatus] = useState<LocalModelStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [checkingLocalModel, setCheckingLocalModel] = useState(false)
  const [creatingSupplier, setCreatingSupplier] = useState(false)
  const [archivingSupplier, setArchivingSupplier] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const visibleSuppliers = useMemo(() => buildVisibleSuppliers(suppliers), [suppliers])

  useEffect(() => {
    void loadSuppliers()
    void loadLocalModelStatus()
  }, [])

  useEffect(() => {
    if (selectedSupplierId === null) {
      return
    }

    void loadSupplier(selectedSupplierId)
  }, [selectedSupplierId])

  useEffect(() => {
    if (supplier === null || !hasActiveAnalysisJob(supplier.analysisJobs)) {
      return
    }

    const intervalId = window.setInterval(() => {
      void loadSupplier(supplier.id)
    }, 3000)

    return () => window.clearInterval(intervalId)
  }, [supplier])

  async function loadSuppliers() {
    setLoading(true)
    setError(null)

    try {
      const result = await getSuppliers()
      const visibleResult = buildVisibleSuppliers(result)
      setSuppliers(result)
      setSelectedSupplierId((current) =>
        current && visibleResult.some((item) => item.id === current)
          ? current
          : visibleResult[0]?.id ?? null,
      )
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setLoading(false)
    }
  }

  async function loadSupplier(id: number) {
    setError(null)

    try {
      setSupplier(normalizeSupplierDetail(await getSupplier(id)))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadLocalModelStatus() {
    setCheckingLocalModel(true)

    try {
      setLocalModelStatus(await getLocalModelStatus())
    } catch (exception) {
      setLocalModelStatus({
        provider: 'Unknown',
        baseUrl: 'Unknown',
        defaultModel: 'Unknown',
        isReachable: false,
        errorMessage: readError(exception),
        models: [],
      })
    } finally {
      setCheckingLocalModel(false)
    }
  }

  async function submitSupplier(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setCreatingSupplier(true)
    setError(null)

    try {
      const createdSupplier = await createSupplier(supplierForm)
      const createdSupplierDetail = normalizeSupplierDetail(await getSupplier(createdSupplier.id))
      const supplierList = await getSuppliers()

      setSuppliers(supplierList)
      setSupplier(createdSupplierDetail)
      setSelectedSupplierId(createdSupplier.id)
      setSupplierForm(emptySupplierForm)
      void loadLocalModelStatus()
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setCreatingSupplier(false)
    }
  }

  async function archiveSelectedSupplier() {
    if (supplier === null) {
      return
    }

    setArchivingSupplier(true)
    setError(null)

    try {
      await archiveSupplier(supplier.id)
      const supplierList = await getSuppliers()
      const visibleResult = buildVisibleSuppliers(supplierList)
      const nextSupplierId = visibleResult.find((item) => item.id !== supplier.id)?.id ?? null

      setSuppliers(supplierList)
      setSelectedSupplierId(nextSupplierId)
      setSupplier(null)

      if (nextSupplierId !== null) {
        await loadSupplier(nextSupplierId)
      }
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setArchivingSupplier(false)
    }
  }

  const assessments = useMemo(
    () =>
      supplier
        ? [...safeArray(supplier.riskAssessments)].sort(
            (a: RiskAssessment, b: RiskAssessment) => Date.parse(b.createdAt) - Date.parse(a.createdAt),
          )
        : [],
    [supplier],
  )
  const latestAssessment = assessments[0]
  const latestAnalysisJob = supplier
    ? [...safeArray(supplier.analysisJobs)].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))[0]
    : undefined
  const latestEvidenceQuality = latestAssessment
    ? readEvidenceQuality(latestAssessment.evidenceSnapshotJson)
    : null
  const latestEvidenceSnapshot = latestAssessment
    ? readEvidenceSnapshot(latestAssessment.evidenceSnapshotJson)
    : null
  const reachableSourceCount = supplier
    ? supplier.sourceChecks.filter((sourceCheck) => sourceCheck.status === 'Reachable').length
    : 0
  const evidenceCompletenessScore = supplier
    ? [supplier.certifications.length > 0, reachableSourceCount > 0, assessments.length > 0].filter(Boolean).length
    : 0
  const nextEvidenceItems = supplier ? buildNextEvidenceItems(supplier, reachableSourceCount) : []
  const knownSupplierFacts = supplier ? buildKnownSupplierFacts(supplier.supplierFacts) : []
  const usefulResearchSources = supplier ? buildUsefulResearchSources(supplier.researchSources) : []

  return (
    <main className="app-shell">
      <section className="sidebar">
        <div className="section-heading">
          <p>Supplier Intelligence</p>
          <span>{visibleSuppliers.length} suppliers</span>
        </div>

        <details className="supplier-create">
          <summary>New supplier</summary>

          <form onSubmit={submitSupplier}>
            <label>
              Name
              <input
                required
                value={supplierForm.name}
                onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })}
              />
            </label>

            <label>
              Country
              <input
                maxLength={2}
                required
                value={supplierForm.countryCode}
                onChange={(event) =>
                  setSupplierForm({ ...supplierForm, countryCode: event.target.value.toUpperCase() })
                }
              />
            </label>

            <label>
              Industry
              <input
                required
                value={supplierForm.industry}
                onChange={(event) => setSupplierForm({ ...supplierForm, industry: event.target.value })}
              />
            </label>

            <label>
              Website
              <input
                placeholder="https://supplier.example"
                type="url"
                value={supplierForm.websiteUrl ?? ''}
                onChange={(event) =>
                  setSupplierForm({
                    ...supplierForm,
                    websiteUrl: event.target.value === '' ? null : event.target.value,
                  })
                }
              />
            </label>

            <label>
              Registry number
              <input
                maxLength={80}
                value={supplierForm.registryNumber ?? ''}
                onChange={(event) =>
                  setSupplierForm({
                    ...supplierForm,
                    registryNumber: event.target.value === '' ? null : event.target.value,
                  })
                }
              />
            </label>

            <label>
              VAT number
              <input
                maxLength={80}
                value={supplierForm.vatNumber ?? ''}
                onChange={(event) =>
                  setSupplierForm({
                    ...supplierForm,
                    vatNumber: event.target.value === '' ? null : event.target.value,
                  })
                }
              />
            </label>

            <label>
              Certification hints
              <textarea
                maxLength={500}
                placeholder="ISO 9001, ISO 14001, IATF 16949"
                rows={3}
                value={supplierForm.certificationHints ?? ''}
                onChange={(event) =>
                  setSupplierForm({
                    ...supplierForm,
                    certificationHints: event.target.value === '' ? null : event.target.value,
                  })
                }
              />
            </label>

            <label className="checkbox-field">
              <input
                checked={supplierForm.runInitialAnalysis}
                type="checkbox"
                onChange={(event) =>
                  setSupplierForm({ ...supplierForm, runInitialAnalysis: event.target.checked })
                }
              />
              Run first analysis
            </label>

            <button className="primary" disabled={creatingSupplier} type="submit">
              {creatingSupplier ? 'Creating and analysing...' : 'Create and analyse'}
            </button>
          </form>
        </details>

        <div className="supplier-list" aria-label="Suppliers">
          {loading && <p className="muted">Loading suppliers...</p>}
          {!loading && visibleSuppliers.length === 0 && <p className="muted">No suppliers found.</p>}
          {visibleSuppliers.map((item) => (
            <button
              className={item.id === selectedSupplierId ? 'supplier-item active' : 'supplier-item'}
              key={item.id}
              onClick={() => setSelectedSupplierId(item.id)}
              type="button"
            >
              <span>{item.name}</span>
              <small>
                {item.countryCode} · {item.industry}
              </small>
              {item.websiteUrl && <small>{item.websiteUrl}</small>}
              <RiskBadge level={item.riskLevel} />
            </button>
          ))}
        </div>
      </section>

      <section className="detail">
        {error && <div className="error-banner">{error}</div>}

        {supplier ? (
          <>
            <div className="supplier-header">
              <div>
                <h1>{supplier.name}</h1>
                <p>
                  {supplier.countryCode} · {supplier.industry}
                </p>
                {supplier.websiteUrl && (
                  <a href={supplier.websiteUrl} rel="noreferrer" target="_blank">
                    {supplier.websiteUrl}
                  </a>
                )}
                <div className="supplier-facts">
                  <Fact label="Registry" value={supplier.registryNumber} />
                  <Fact label="VAT" value={supplier.vatNumber} />
                  <Fact label="Certification hints" value={supplier.certificationHints} />
                </div>
              </div>
              <div className="supplier-header-actions">
                <RiskBadge level={supplier.riskLevel} />
                <button
                  className="ghost"
                  disabled={archivingSupplier}
                  onClick={archiveSelectedSupplier}
                  type="button"
                >
                  {archivingSupplier ? 'Archiving...' : 'Archive'}
                </button>
              </div>
            </div>

            <div className="metric-grid">
              <Metric label="Certifications" value={supplier.certifications.length} />
              <Metric label="Evidence sources" value={supplier.sourceChecks.length} />
              <Metric label="Assessments" value={supplier.riskAssessments.length} />
            </div>

            <section className="workflow-strip">
              <WorkflowStep
                isDone={supplier.certifications.length > 0}
                label="1. Certification"
                value={`${supplier.certifications.length} discovered`}
              />
              <WorkflowStep
                isDone={supplier.sourceChecks.length > 0}
                label="2. Public evidence"
                value={`${supplier.sourceChecks.length} sources`}
              />
              <WorkflowStep
                isDone={localModelStatus?.isReachable ?? false}
                label="3. Intelligence review"
                value={assessments.length > 0 ? 'available' : 'pending'}
              />
              <WorkflowStep
                isDone={latestAnalysisJob?.status === 'Completed' || assessments.length > 0}
                label="4. Risk decision"
                value={
                  latestAnalysisJob && latestAnalysisJob.status !== 'Completed'
                    ? latestAnalysisJob.status
                    : latestAssessment
                      ? `${latestAssessment.riskLevel} / ${latestAssessment.score}`
                      : 'none'
                }
              />
            </section>

            {latestEvidenceQuality && (
              <section className="quality-panel">
                <div className="section-heading">
                  <p>Verification status</p>
                  <span>{latestEvidenceQuality.band}</span>
                </div>
                <div className="quality-grid">
                  <StatusField label="Evidence strength" value={`${latestEvidenceQuality.score}/100`} />
                  <StatusField label="Certification verified" value={formatBoolean(latestEvidenceQuality.hasVerifiedCertification)} />
                  <StatusField label="Registry verified" value={formatBoolean(latestEvidenceQuality.hasReachableRegistrySource)} />
                  <StatusField label="VAT verified" value={formatBoolean(latestEvidenceQuality.hasReachableVatSource)} />
                </div>
              </section>
            )}

            {latestEvidenceSnapshot && (
              <section className="supplier-intelligence-panel">
                <div className="section-heading">
                  <p>Supplier snapshot</p>
                  <span>{latestEvidenceSnapshot.supplierProfile.isSparseInput ? 'Limited public profile' : 'Detailed public profile'}</span>
                </div>
                <div className="supplier-intelligence-grid">
                  <StatusField label="Profile completeness" value={`${latestEvidenceSnapshot.supplierProfile.inputDepth}/7`} />
                  <StatusField label="Current assessment" value={latestEvidenceSnapshot.riskDecision.reason} />
                </div>
                <p className="company-description">{latestEvidenceSnapshot.companySummary.description}</p>
                <SnapshotList title="External evidence" items={latestEvidenceSnapshot.companySummary.externalHighlights} />
                <SnapshotList title="Own website evidence" items={latestEvidenceSnapshot.companySummary.ownWebsiteHighlights} />
                <SnapshotList title="Known facts" items={latestEvidenceSnapshot.automationFindings} />
              </section>
            )}

            {(knownSupplierFacts.length > 0 || usefulResearchSources.length > 0) && (
              <section className="supplier-intelligence-panel">
                <div className="section-heading">
                  <p>Validated facts</p>
                  <span>{knownSupplierFacts.length} facts</span>
                </div>
                <SnapshotList title="Supplier facts" items={knownSupplierFacts} />
                <SnapshotList title="Research sources" items={usefulResearchSources} />
              </section>
            )}

            {nextEvidenceItems.length > 0 && (
              <section className="missing-facts-panel">
                <div className="section-heading">
                  <p>Missing facts</p>
                  <span>{nextEvidenceItems.length} open</span>
                </div>
                <ul>
                  {nextEvidenceItems.map((item) => (
                    <li key={item}>{item}</li>
                  ))}
                </ul>
              </section>
            )}

            <section className="evidence-panel">
              <div className="workflow-card">
                <div className="section-heading">
                  <p>Certifications</p>
                  <span>{supplier.certifications.length} records</span>
                </div>
                <CompactCertificationList certifications={supplier.certifications} />
                {latestEvidenceSnapshot && (
                  <SnapshotList title="Expected certifications and documents" items={latestEvidenceSnapshot.expectedEvidence} />
                )}
              </div>

              <div className="workflow-card">
                <div className="section-heading">
                  <p>Evidence</p>
                  <span>{supplier.sourceChecks.length} sources</span>
                </div>
                <CompactSourceCheckList sourceChecks={supplier.sourceChecks} />
              </div>
            </section>

            <section className="workspace read-only-workspace">
              <div className="assessment-list">
                <div className="section-heading">
                  <p>Risk summary</p>
                  <span>{assessments.length} total</span>
                </div>

                {assessments.length === 0 ? (
                  <p className="muted evidence-empty">No assessments yet.</p>
                ) : (
                  assessments.map((assessment) => (
                    <article className="assessment" key={assessment.id}>
                      <div className="assessment-topline">
                        <RiskBadge level={assessment.riskLevel} />
                        <strong>{assessment.score}/100</strong>
                        <span>
                        Supplier intelligence
                      </span>
                      </div>
                      <p className="assessment-summary">{assessment.summaryMarkdown}</p>
                      <details className="assessment-full-text">
                        <summary>Detailed reasoning</summary>
                        <pre>{assessment.summaryMarkdown}</pre>
                      </details>
                    </article>
                  ))
                )}
              </div>
            </section>

            <details className="technical-details">
              <summary>Technical details</summary>
              <section className="analysis-job-panel">
                <div className="section-heading">
                  <p>Analysis jobs</p>
                  <span>{supplier.analysisJobs.length} total</span>
                </div>
                <AnalysisJobList jobs={supplier.analysisJobs} />
              </section>

              <section className="local-model-panel">
                <div className="section-heading">
                  <p>Local model runtime</p>
                  <span>{checkingLocalModel ? 'checking' : localModelStatus?.isReachable ? 'online' : 'offline'}</span>
                </div>
                <div className="local-model-grid">
                  <StatusField label="Provider" value={localModelStatus?.provider ?? 'Unknown'} />
                  <StatusField label="Default model" value={localModelStatus?.defaultModel ?? 'Unknown'} />
                  <StatusField label="Base URL" value={localModelStatus?.baseUrl ?? 'Unknown'} />
                  <StatusField label="Evidence score" value={`${evidenceCompletenessScore}/3`} />
                </div>
                {localModelStatus?.errorMessage && <p className="local-model-error">{localModelStatus.errorMessage}</p>}
                {localModelStatus?.models.length ? (
                  <div className="model-list">
                    {localModelStatus.models.map((model) => (
                      <span key={model.name}>{model.name}</span>
                    ))}
                  </div>
                ) : (
                  <p className="muted local-model-empty">No local models reported.</p>
                )}
              </section>
            </details>
          </>
        ) : (
          <p className="muted">Select a supplier.</p>
        )}
      </section>
    </main>
  )
}

function RiskBadge({ level }: { level: RiskLevel }) {
  return <span className={`risk-badge risk-${level.toLowerCase()}`}>{level}</span>
}

function WorkflowStep({ isDone, label, value }: { isDone: boolean; label: string; value: string }) {
  return (
    <div className={isDone ? 'workflow-step done' : 'workflow-step'}>
      <strong>{label}</strong>
      <span>{value}</span>
    </div>
  )
}

function Metric({ label, value }: { label: string; value: number }) {
  return (
    <div className="metric">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  )
}

function StatusField({ label, value }: { label: string; value: string }) {
  return (
    <div className="status-field">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function Fact({ label, value }: { label: string; value: string | null }) {
  if (!value) {
    return null
  }

  return (
    <span>
      <strong>{label}</strong>
      {value}
    </span>
  )
}

function hasActiveAnalysisJob(jobs: AnalysisJob[]) {
  return safeArray(jobs).some((job) => job.status === 'Queued' || job.status === 'Running')
}

function safeArray<T>(value: T[] | null | undefined) {
  return Array.isArray(value) ? value : []
}

function normalizeSupplierDetail(detail: SupplierDetail): SupplierDetail {
  return {
    ...detail,
    isArchived: detail.isArchived ?? false,
    analysisJobs: safeArray(detail.analysisJobs),
    certifications: safeArray(detail.certifications),
    sourceChecks: safeArray(detail.sourceChecks),
    researchSources: safeArray(detail.researchSources),
    supplierFacts: safeArray(detail.supplierFacts),
    riskAssessments: safeArray(detail.riskAssessments),
  }
}

function buildVisibleSuppliers(items: SupplierSummary[]) {
  const visible = new Map<string, SupplierSummary>()

  for (const item of items) {
    if (isDevelopmentSupplier(item)) {
      continue
    }

    const key = buildSupplierIdentityKey(item)
    if (!visible.has(key)) {
      visible.set(key, item)
    }
  }

  return [...visible.values()]
}

function buildSupplierIdentityKey(item: SupplierSummary) {
  return [
    normalizeIdentityPart(item.name),
    item.countryCode.trim().toUpperCase(),
    normalizeIdentityPart(item.industry),
  ].join('|')
}

function normalizeIdentityPart(value: string) {
  return value.trim().toLowerCase().replace(/\s+/g, ' ')
}

function isDevelopmentSupplier(item: SupplierSummary) {
  const name = item.name.toLowerCase()
  const website = item.websiteUrl?.toLowerCase() ?? ''

  return name.includes('smoke') ||
    name.includes('debug') ||
    name.includes('demo') ||
    name.includes('prompt') ||
    name.includes('learning') ||
    name.includes('test') ||
    website.includes('127.0.0.1') ||
    website.includes('example.com') ||
    website.includes('learning-demo')
}

function buildKnownSupplierFacts(facts: SupplierFact[]) {
  return distinctText(safeArray(facts)
    .filter((fact) => fact.factType !== 'MissingEvidence' && fact.factType !== 'SourceLimitation')
    .map((fact) => `${formatFactType(fact.factType)}: ${shortenText(fact.value, 260)}`))
    .slice(0, 8)
}

function buildUsefulResearchSources(sources: ResearchSource[]) {
  return distinctText(safeArray(sources)
    .filter((source) => source.relevance !== 'Low' || source.status !== 'Reachable')
    .map((source) => `${source.sourceName} (${source.status}, ${source.relevance}): ${shortenText(source.summary, 220)}`))
    .slice(0, 6)
}

function distinctText(items: string[]) {
  const seen = new Set<string>()
  return items.filter((item) => {
    const key = item.trim().toLowerCase()
    if (seen.has(key)) {
      return false
    }

    seen.add(key)
    return true
  })
}

function formatFactType(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, (character) => character.toUpperCase())
}

function shortenText(value: string, maxLength: number) {
  const normalized = value.replace(/\s+/g, ' ').trim()
  return normalized.length <= maxLength ? normalized : `${normalized.slice(0, maxLength - 3).trim()}...`
}

type EvidenceQuality = {
  score: number
  band: string
  hasVerifiedCertification: boolean
  hasWebsiteCertificationClaim: boolean
  hasReachableWebsite: boolean
  hasReachableRegistrySource: boolean
  hasReachableVatSource: boolean
  hasBlockedOrFailedSource: boolean
}

type EvidenceSnapshot = {
  supplierProfile: {
    inputDepth: number
    isSparseInput: boolean
  }
  riskDecision: {
    reason: string
  }
  companySummary: {
    description: string
    ownWebsiteHighlights: string[]
    externalHighlights: string[]
  }
  automationFindings: string[]
  expectedEvidence: string[]
  recommendedNextChecks: string[]
}

function readEvidenceQuality(snapshotJson: string | null): EvidenceQuality | null {
  if (!snapshotJson) {
    return null
  }

  try {
    const parsed = JSON.parse(snapshotJson) as { evidenceQuality?: EvidenceQuality }
    return parsed.evidenceQuality ?? null
  } catch {
    return null
  }
}

function readEvidenceSnapshot(snapshotJson: string | null): EvidenceSnapshot | null {
  if (!snapshotJson) {
    return null
  }

  try {
    const parsed = JSON.parse(snapshotJson) as Partial<EvidenceSnapshot>

    if (!parsed.supplierProfile || !parsed.riskDecision) {
      return null
    }

    return {
      supplierProfile: {
        inputDepth: parsed.supplierProfile.inputDepth ?? 0,
        isSparseInput: parsed.supplierProfile.isSparseInput ?? false,
      },
      riskDecision: {
        reason: parsed.riskDecision.reason ?? 'No decision reason stored.',
      },
      companySummary: {
        description:
          parsed.companySummary?.description ??
          'No company description found in the available evidence yet.',
        ownWebsiteHighlights: parsed.companySummary?.ownWebsiteHighlights ?? [],
        externalHighlights: parsed.companySummary?.externalHighlights ?? [],
      },
      automationFindings: parsed.automationFindings ?? [],
      expectedEvidence: parsed.expectedEvidence ?? [],
      recommendedNextChecks: parsed.recommendedNextChecks ?? [],
    }
  } catch {
    return null
  }
}

function formatBoolean(value: boolean) {
  return value ? 'Yes' : 'No'
}

function SnapshotList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <div className="snapshot-list">
      <strong>{title}</strong>
      <ul>
        {items.map((item, index) => (
          <li key={`${item}-${index}`}>{item}</li>
        ))}
      </ul>
    </div>
  )
}

function AnalysisJobList({ jobs }: { jobs: AnalysisJob[] }) {
  if (jobs.length === 0) {
    return <p className="muted evidence-empty">No analysis jobs stored.</p>
  }

  return (
    <div className="analysis-job-list">
      {[...jobs]
        .sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
        .map((job) => (
          <article className={`analysis-job analysis-${job.status.toLowerCase()}`} key={job.id}>
            <div>
              <strong>{job.status}</strong>
              <span>{job.jobType}</span>
            </div>
            <p>{job.progressMessage}</p>
            {job.errorMessage && <p>{job.errorMessage}</p>}
          </article>
        ))}
    </div>
  )
}

function buildNextEvidenceItems(supplier: SupplierDetail, reachableSourceCount: number) {
  const items: string[] = []

  if (!supplier.certifications.some((certification) => certification.isVerified)) {
    items.push('No verified certification found.')
  }

  if (!supplier.sourceChecks.some((sourceCheck) => sourceCheck.sourceName.toLowerCase().includes('registry'))) {
    items.push(
      supplier.registryNumber || supplier.vatNumber
        ? 'Provided registry or VAT identifier still needs verification.'
        : 'Company registry record not verified.',
    )
  }

  if (reachableSourceCount === 0) {
    items.push('No public source confirmed.')
  }

  if (supplier.sourceChecks.some((sourceCheck) => sourceCheck.status === 'Blocked' || sourceCheck.status === 'Failed')) {
    items.push('Some sources could not be verified.')
  }

  return items
}

function CompactCertificationList({ certifications }: { certifications: Certification[] }) {
  if (certifications.length === 0) {
    return <p className="muted evidence-empty">No verified certifications found yet.</p>
  }

  return (
    <div className="compact-list">
      {certifications.map((certification) => (
        <article className="certification-item" key={certification.id}>
          <strong>{certification.standard}</strong>
          <span>{certification.isVerified ? 'Verified' : 'Unverified'}</span>
          <p>{certification.issuer}</p>
          {certification.verificationNotes && <p>{certification.verificationNotes}</p>}
          <small>{certification.validUntil ? `Valid until ${certification.validUntil}` : 'No expiry date'}</small>
        </article>
      ))}
    </div>
  )
}

function CompactSourceCheckList({ sourceChecks }: { sourceChecks: SourceCheck[] }) {
  if (sourceChecks.length === 0) {
    return <p className="muted evidence-empty">No public sources verified yet.</p>
  }

  return (
    <div className="compact-list">
      {[...sourceChecks]
        .sort(compareSourceChecks)
        .map((sourceCheck) => {
          const quality = scoreSourceCheck(sourceCheck)

          return (
            <article className="evidence-item" key={sourceCheck.id}>
              <div className="evidence-title-row">
                <strong>{sourceCheck.sourceName}</strong>
                <span className={`source-quality source-quality-${quality.level.toLowerCase()}`}>
                  {quality.level} · {quality.score}/100
                </span>
              </div>
              <div className="source-meta-row">
                <span>{formatSourceStatus(sourceCheck.status)}</span>
                <span>{formatSourceKind(sourceCheck)}</span>
              </div>
              <a href={sourceCheck.url} rel="noreferrer" target="_blank">
                {sourceCheck.url}
              </a>
              <p>{shortenText(sourceCheck.notes || 'No notes', 420)}</p>
              <div className="source-card-actions">
                <a className="source-open-link" href={sourceCheck.url} rel="noreferrer" target="_blank">
                  Open source
                </a>
              </div>
            </article>
          )
        })}
    </div>
  )
}

function compareSourceChecks(a: SourceCheck, b: SourceCheck) {
  return scoreSourceCheck(b).score - scoreSourceCheck(a).score ||
    Date.parse(b.checkedAt) - Date.parse(a.checkedAt)
}

function scoreSourceCheck(sourceCheck: SourceCheck) {
  let score = 15
  const name = sourceCheck.sourceName.toLowerCase()
  const notes = sourceCheck.notes.toLowerCase()
  const url = sourceCheck.url.toLowerCase()

  if (sourceCheck.status === 'Reachable') {
    score += 25
  }

  if (name.includes('supplier website') || name.includes('website research')) {
    score += 18
  }

  if (name.includes('ai web search')) {
    score += url.includes('openrouter.ai') ? 10 : 18
  }

  if (name.includes('registry') || name.includes('vat') || name.includes('vies')) {
    score += 22
  }

  if (notes.includes('source urls:') || notes.includes('http')) {
    score += 10
  }

  if (notes.includes('certificate') || notes.includes('iso ') || notes.includes('iatf') || notes.includes('as9100')) {
    score += 8
  }

  if (sourceCheck.status === 'Blocked' || sourceCheck.status === 'Failed') {
    score -= 25
  }

  if (url.includes('openrouter.ai/search')) {
    score -= 12
  }

  const boundedScore = Math.max(0, Math.min(100, score))
  const level = boundedScore >= 70 ? 'High' : boundedScore >= 40 ? 'Medium' : 'Low'

  return { level, score: boundedScore }
}

function formatSourceKind(sourceCheck: SourceCheck) {
  const sourceName = sourceCheck.sourceName.toLowerCase()
  const url = sourceCheck.url.toLowerCase()

  if (sourceName.includes('registry') || sourceName.includes('vat') || sourceName.includes('vies')) {
    return 'registry'
  }

  if (sourceName.includes('supplier website') || sourceName.includes('website research')) {
    return 'own website'
  }

  if (sourceName.includes('ai web search')) {
    return url.includes('openrouter.ai') ? 'search summary' : 'external source'
  }

  if (sourceName.includes('cert')) {
    return 'certification'
  }

  return 'public source'
}

function readError(exception: unknown) {
  return exception instanceof Error ? exception.message : 'Unexpected error'
}

function formatSourceStatus(status: string) {
  if (status === 'Reachable') {
    return 'Available'
  }

  if (status === 'Blocked') {
    return 'Could not access'
  }

  if (status === 'Failed') {
    return 'Verification failed'
  }

  return status
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
