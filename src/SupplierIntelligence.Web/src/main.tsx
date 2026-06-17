import { StrictMode, useEffect, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import {
  archiveSupplier,
  confirmSupplierMatchCandidate,
  createSupplier,
  getSupplierAnalytics,
  getSupplierAnalysisJob,
  getSupplierAnalysisJobs,
  getSupplierMatchCandidates,
  getLocalModelStatus,
  getSupplier,
  getSupplierReviewSummary,
  getSuppliers,
  queueSupplierAnalysis,
  rejectSupplierMatchCandidate,
  suggestSupplierMatchCandidates,
  type ReviewStepName,
  type AnalysisJob,
  type Certification,
  type CreateSupplierInput,
  type LocalModelStatus,
  type RiskAssessment,
  type RiskLevel,
  type ResearchSource,
  type SourceCheck,
  type SourceMixItem,
  type SupplierFact,
  type SupplierAnalytics,
  type SupplierDetail,
  type SupplierMatchCandidate,
  type SupplierReviewSummary,
  type SupplierSummary,
  type TimelineItem,
  type TrustBreakdownItem,
} from './api.ts'
import './styles.css'

const emptySupplierForm: CreateSupplierInput = {
  name: '',
  countryCode: '',
  industry: '',
  websiteUrl: '',
  runInitialAnalysis: true,
}

type ReviewStep = ReviewStepName

function App() {
  const [suppliers, setSuppliers] = useState<SupplierSummary[]>([])
  const [selectedSupplierId, setSelectedSupplierId] = useState<number | null>(null)
  const [supplier, setSupplier] = useState<SupplierDetail | null>(null)
  const [reviewSummary, setReviewSummary] = useState<SupplierReviewSummary | null>(null)
  const [supplierAnalytics, setSupplierAnalytics] = useState<SupplierAnalytics | null>(null)
  const [matchCandidates, setMatchCandidates] = useState<SupplierMatchCandidate[]>([])
  const [supplierForm, setSupplierForm] = useState<CreateSupplierInput>(emptySupplierForm)
  const [localModelStatus, setLocalModelStatus] = useState<LocalModelStatus | null>(null)
  const [activeAnalysisJob, setActiveAnalysisJob] = useState<AnalysisJob | null>(null)
  const [loading, setLoading] = useState(true)
  const [checkingLocalModel, setCheckingLocalModel] = useState(false)
  const [creatingSupplier, setCreatingSupplier] = useState(false)
  const [archivingSupplier, setArchivingSupplier] = useState(false)
  const [queueingAnalysis, setQueueingAnalysis] = useState(false)
  const [loadingMatchCandidates, setLoadingMatchCandidates] = useState(false)
  const [reviewingMatchCandidateId, setReviewingMatchCandidateId] = useState<number | null>(null)
  const [activeReviewStep, setActiveReviewStep] = useState<ReviewStep>('identity')
  const [error, setError] = useState<string | null>(null)
  const visibleSuppliers = useMemo(() => buildVisibleSuppliers(suppliers), [suppliers])

  useEffect(() => {
    void loadSuppliers()
    void loadLocalModelStatus()
  }, [])

  useEffect(() => {
    if (selectedSupplierId === null) {
      setReviewSummary(null)
      setSupplierAnalytics(null)
      setActiveAnalysisJob(null)
      setMatchCandidates([])
      return
    }

    setActiveReviewStep('identity')
    void loadSupplier(selectedSupplierId)
    void loadReviewSummary(selectedSupplierId)
    void loadSupplierAnalytics(selectedSupplierId)
    void loadAnalysisJobs(selectedSupplierId)
    void loadMatchCandidates(selectedSupplierId)
  }, [selectedSupplierId])

  useEffect(() => {
    if (supplier === null || !activeAnalysisJob || !isActiveAnalysisJob(activeAnalysisJob)) {
      return
    }

    const intervalId = window.setInterval(() => {
      void refreshAnalysisRun(supplier.id, activeAnalysisJob.id)
    }, 3000)

    return () => window.clearInterval(intervalId)
  }, [supplier, activeAnalysisJob])

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

  async function loadReviewSummary(id: number) {
    setError(null)

    try {
      setReviewSummary(await getSupplierReviewSummary(id))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadSupplierAnalytics(id: number) {
    setError(null)

    try {
      setSupplierAnalytics(await getSupplierAnalytics(id))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadAnalysisJobs(supplierId: number) {
    setError(null)

    try {
      const jobs = await getSupplierAnalysisJobs(supplierId)
      setActiveAnalysisJob(findActiveOrLatestAnalysisJob(jobs))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadMatchCandidates(supplierId: number, quiet = false) {
    if (!quiet) {
      setLoadingMatchCandidates(true)
    }
    setError(null)

    try {
      setMatchCandidates(await getSupplierMatchCandidates(supplierId))
    } catch (exception) {
      setError(readError(exception))
    } finally {
      if (!quiet) {
        setLoadingMatchCandidates(false)
      }
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
      setMatchCandidates([])
      await loadReviewSummary(createdSupplier.id)
      await loadSupplierAnalytics(createdSupplier.id)
      await loadMatchCandidates(createdSupplier.id)
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
      setReviewSummary(null)
      setSupplierAnalytics(null)
      setMatchCandidates([])

      if (nextSupplierId !== null) {
        await loadSupplier(nextSupplierId)
        await loadReviewSummary(nextSupplierId)
        await loadSupplierAnalytics(nextSupplierId)
        await loadMatchCandidates(nextSupplierId)
      }
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setArchivingSupplier(false)
    }
  }

  async function runAnalysis() {
    if (supplier === null) {
      return
    }

    setQueueingAnalysis(true)
    setError(null)

    try {
      const job = await queueSupplierAnalysis(supplier.id)
      setActiveAnalysisJob(job)
      await refreshAnalysisRun(supplier.id, job.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setQueueingAnalysis(false)
    }
  }

  async function refreshAnalysisRun(supplierId: number, jobId: number) {
    setError(null)

    try {
      const [job] = await Promise.all([
        getSupplierAnalysisJob(supplierId, jobId),
        loadSupplier(supplierId),
        loadReviewSummary(supplierId),
        loadSupplierAnalytics(supplierId),
        loadMatchCandidates(supplierId, true),
      ])

      setActiveAnalysisJob(job)
      if (!isActiveAnalysisJob(job)) {
        void loadSuppliers()
      }
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function findMatchCandidates() {
    if (supplier === null) {
      return
    }

    setLoadingMatchCandidates(true)
    setError(null)

    try {
      setMatchCandidates(await suggestSupplierMatchCandidates(supplier.id))
      await loadReviewSummary(supplier.id)
      await loadSupplierAnalytics(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setLoadingMatchCandidates(false)
    }
  }

  async function confirmMatchCandidate(candidateId: number) {
    if (supplier === null) {
      return
    }

    setReviewingMatchCandidateId(candidateId)
    setError(null)

    try {
      await confirmSupplierMatchCandidate(supplier.id, candidateId)
      await loadMatchCandidates(supplier.id)
      await loadReviewSummary(supplier.id)
      await loadSupplierAnalytics(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setReviewingMatchCandidateId(null)
    }
  }

  async function rejectMatchCandidate(candidateId: number) {
    if (supplier === null) {
      return
    }

    setReviewingMatchCandidateId(candidateId)
    setError(null)

    try {
      await rejectSupplierMatchCandidate(supplier.id, candidateId)
      await loadMatchCandidates(supplier.id)
      await loadReviewSummary(supplier.id)
      await loadSupplierAnalytics(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setReviewingMatchCandidateId(null)
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
  const visibleMatchCandidates = buildVisibleMatchCandidates(matchCandidates)
  const confirmedIdentity = matchCandidates.find((candidate) => candidate.status === 'Confirmed') ?? null
  const hasConfirmedIdentity = confirmedIdentity !== null
  const knownInformation = supplier
    ? buildKnownInformation({
        supplier,
        confirmedIdentity,
        latestAssessment,
        latestEvidenceQuality,
        reachableSourceCount,
      })
    : []
  const riskMemoItems = buildRiskMemoItems({
    hasConfirmedIdentity,
    latestAssessment,
    latestEvidenceQuality,
    nextEvidenceItems,
  })
  const nextAction = buildNextAction({
    hasConfirmedIdentity,
    loadingMatchCandidates,
    matchCandidateCount: visibleMatchCandidates.length,
    nextEvidenceCount: nextEvidenceItems.length,
    latestAssessment,
  })
  const displayKnownInformation = reviewSummary?.knownInformation ?? knownInformation
  const displayMissingInformation = reviewSummary?.missingInformation ?? nextEvidenceItems
  const displayNextAction = reviewSummary?.nextAction ?? nextAction
  const displayTrustSignals = reviewSummary?.trustSignals ?? null

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
                  <Fact label="Country" value={supplier.countryCode} />
                  <Fact label="Industry" value={supplier.industry} />
                </div>
              </div>
              <div className="supplier-header-actions">
                <RiskBadge level={supplier.riskLevel} />
                <button
                  className="primary"
                  disabled={queueingAnalysis || isActiveAnalysisJob(activeAnalysisJob)}
                  onClick={runAnalysis}
                  type="button"
                >
                  {queueingAnalysis
                    ? 'Queueing...'
                    : isActiveAnalysisJob(activeAnalysisJob)
                      ? 'Analysis running'
                      : 'Run analysis'}
                </button>
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

            <section className="review-overview">
              <div className="next-action-card">
                <div className="section-heading">
                  <p>Next action</p>
                  <span>{reviewSummary?.headline ?? nextAction.status}</span>
                </div>
                <h2>{displayNextAction.title}</h2>
                <p>{displayNextAction.description}</p>
                <button
                  className="primary"
                  disabled={!hasConfirmedIdentity && loadingMatchCandidates}
                  onClick={() => {
                    if (displayNextAction.step === 'identity' && !hasConfirmedIdentity) {
                      setActiveReviewStep('identity')
                      void findMatchCandidates()
                      return
                    }

                    setActiveReviewStep(displayNextAction.step)
                  }}
                  type="button"
                >
                  {!hasConfirmedIdentity && loadingMatchCandidates ? 'Finding matches...' : displayNextAction.buttonLabel}
                </button>
              </div>

              <section className="dashboard-card">
                <div className="section-heading">
                  <p>What we know</p>
                  <span>{displayTrustSignals?.identity ?? (hasConfirmedIdentity ? 'Identity saved' : 'Needs decision')}</span>
                </div>
                <KnownInformationList items={displayKnownInformation} />
                {displayTrustSignals && (
                  <div className="trust-signal-row">
                    <StatusField label="Evidence" value={displayTrustSignals.evidence} />
                    <StatusField label="Certifications" value={displayTrustSignals.certifications} />
                    <StatusField label="Risk" value={displayTrustSignals.risk} />
                  </div>
                )}
                {supplierAnalytics && (
                  <TrustBreakdown
                    compact
                    overallTrustScore={supplierAnalytics.overallTrustScore}
                    items={supplierAnalytics.trustBreakdown}
                  />
                )}
              </section>
            </section>

            {activeAnalysisJob && (
              <AnalysisRunPanel
                analytics={supplierAnalytics}
                job={activeAnalysisJob}
                matchCandidates={matchCandidates}
                supplier={supplier}
                latestAssessment={latestAssessment}
              />
            )}

            <nav className="review-stepper" aria-label="Supplier review steps">
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Identity"
                step="identity"
                summary={summarizeMatchCandidates(visibleMatchCandidates)}
                onSelect={setActiveReviewStep}
              />
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Evidence"
                step="evidence"
                summary={`${supplier.sourceChecks.length} sources`}
                onSelect={setActiveReviewStep}
              />
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Risk"
                step="risk"
                summary={latestAssessment ? latestAssessment.riskLevel : 'None'}
                onSelect={setActiveReviewStep}
              />
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Report"
                step="report"
                summary={hasConfirmedIdentity ? 'Draft later' : 'Blocked'}
                onSelect={setActiveReviewStep}
              />
            </nav>

            <section className="review-workspace">
              {activeReviewStep === 'identity' && (
                <section className="match-candidate-panel">
                  <div className="section-heading">
                    <p>{hasConfirmedIdentity ? 'Saved identity' : 'Possible identities'}</p>
                    <span>{summarizeMatchCandidates(visibleMatchCandidates)}</span>
                  </div>
                  {confirmedIdentity && (
                    <div className="saved-identity-banner">
                      <strong>{formatCandidateDisplayName(confirmedIdentity.candidateName)}</strong>
                      <span>Saved as the confirmed supplier identity.</span>
                    </div>
                  )}
                  <div className="match-candidate-toolbar">
                    <p>
                      Confirming saves the selected legal entity on this supplier record. Rejected identities stay dismissed.
                    </p>
                    <button
                      className="primary"
                      disabled={loadingMatchCandidates}
                      onClick={findMatchCandidates}
                      type="button"
                    >
                      {loadingMatchCandidates ? 'Finding matches...' : 'Find matches'}
                    </button>
                  </div>
                  <MatchCandidateList
                    candidates={visibleMatchCandidates}
                    isLoading={loadingMatchCandidates}
                    reviewingCandidateId={reviewingMatchCandidateId}
                    onConfirm={confirmMatchCandidate}
                    onReject={rejectMatchCandidate}
                  />
                </section>
              )}

              {activeReviewStep === 'evidence' && (
                <>
                  {displayMissingInformation.length > 0 && (
                    <section className="missing-facts-panel">
                      <div className="section-heading">
                        <p>Missing facts</p>
                        <span>{displayMissingInformation.length} open</span>
                      </div>
                      <ul>
                        {displayMissingInformation.map((item) => (
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

                  {(knownSupplierFacts.length > 0 || usefulResearchSources.length > 0) && (
                    <details className="secondary-details">
                      <summary>Validated facts and research notes</summary>
                      <section className="supplier-intelligence-panel">
                        <SnapshotList title="Supplier facts" items={knownSupplierFacts} />
                        <SnapshotList title="Research sources" items={usefulResearchSources} />
                      </section>
                    </details>
                  )}
                </>
              )}

              {activeReviewStep === 'risk' && (
                <>
                  <section className="risk-memo-panel">
                    <div className="section-heading">
                      <p>Risk memo</p>
                      <span>{latestAssessment ? latestAssessment.riskLevel : 'No assessment'}</span>
                    </div>
                    <div className="risk-memo-headline">
                      <RiskBadge level={latestAssessment?.riskLevel ?? 'Unknown'} />
                      <strong>{formatRiskAssessmentScore(latestAssessment)}</strong>
                    </div>
                    <ul>
                      {riskMemoItems.map((item) => (
                        <li key={item}>{item}</li>
                      ))}
                    </ul>
                  </section>

                  <details className="secondary-details">
                    <summary>Evidence behind this risk memo</summary>
                    {latestEvidenceQuality && (
                      <section className="quality-panel">
                        <div className="section-heading">
                          <p>Verification status</p>
                          <span>{latestEvidenceQuality.band}</span>
                        </div>
                        <div className="quality-grid">
                          <StatusField label="Evidence strength" value={`${latestEvidenceQuality.score}/100`} />
                          <StatusField label="Certification verified" value={formatBoolean(latestEvidenceQuality.hasVerifiedCertification)} />
                          <StatusField label="Registration evidence" value={formatBoolean(latestEvidenceQuality.hasReachableRegistrySource)} />
                          <StatusField label="Tax evidence" value={formatBoolean(latestEvidenceQuality.hasReachableVatSource)} />
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
                  </details>

                  <details className="secondary-details">
                    <summary>Full risk assessment text</summary>
                    <section className="workspace read-only-workspace">
                      <div className="assessment-list">
                        {assessments.length === 0 ? (
                          <p className="muted evidence-empty">No assessments yet.</p>
                        ) : (
                          assessments.map((assessment) => (
                            <article className="assessment" key={assessment.id}>
                              <div className="assessment-topline">
                                <RiskBadge level={assessment.riskLevel} />
                                <strong>{formatRiskAssessmentScore(assessment)}</strong>
                                <span>Supplier intelligence</span>
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
                  </details>
                </>
              )}

              {activeReviewStep === 'report' && (
                <section className="report-panel">
                  <div className="section-heading">
                    <p>Report</p>
                    <span>{hasConfirmedIdentity ? 'Draft-ready' : 'Blocked'}</span>
                  </div>
                  <div className="report-grid">
                    <StatusField label="Supplier profile" value={supplier.name} />
                    <StatusField label="Confirmed identity" value={hasConfirmedIdentity ? 'Yes' : 'No'} />
                    <StatusField label="Evidence sources" value={`${supplier.sourceChecks.length}`} />
                    <StatusField label="Risk decision" value={latestAssessment ? latestAssessment.riskLevel : 'None'} />
                  </div>
                  {latestEvidenceSnapshot ? (
                    <SnapshotList title="Recommended next checks" items={latestEvidenceSnapshot.recommendedNextChecks} />
                  ) : (
                    <p className="muted evidence-empty">No report recommendations available yet.</p>
                  )}

                  {supplierAnalytics && (
                    <SupplierAnalyticsPanel analytics={supplierAnalytics} />
                  )}

                  <details className="secondary-details">
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
                        <p>Model runtime</p>
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
                        <p className="muted local-model-empty">No provider models reported.</p>
                      )}
                    </section>
                  </details>
                </section>
              )}
            </section>
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

function ReviewStepButton({
  activeStep,
  label,
  step,
  summary,
  onSelect,
}: {
  activeStep: ReviewStep
  label: string
  step: ReviewStep
  summary: string
  onSelect: (step: ReviewStep) => void
}) {
  return (
    <button
      className={activeStep === step ? 'review-step active' : 'review-step'}
      onClick={() => onSelect(step)}
      type="button"
    >
      <span>{label}</span>
      <strong>{summary}</strong>
    </button>
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

function KnownInformationList({ items }: { items: string[] }) {
  return (
    <ul className="known-info-list">
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  )
}

function TrustBreakdown({
  compact = false,
  overallTrustScore,
  items,
}: {
  compact?: boolean
  overallTrustScore: number
  items: TrustBreakdownItem[]
}) {
  return (
    <div className={compact ? 'trust-breakdown compact' : 'trust-breakdown'}>
      <div className="trust-score-header">
        <strong>Trust score</strong>
        <span>{overallTrustScore}/100</span>
      </div>
      <div className="trust-bar-list">
        {items.map((item) => (
          <div className="trust-bar-row" key={item.label}>
            <div className="trust-bar-label">
              <strong>{item.label}</strong>
              <span>{item.status}</span>
            </div>
            <div className="trust-bar-track" aria-label={`${item.label}: ${item.score} out of 100`}>
              <span style={{ width: `${item.score}%` }} />
            </div>
            {!compact && <p>{item.explanation}</p>}
          </div>
        ))}
      </div>
    </div>
  )
}

function SupplierAnalyticsPanel({ analytics }: { analytics: SupplierAnalytics }) {
  return (
    <section className="analytics-panel">
      <div className="section-heading">
        <p>Analytics</p>
        <span>{analytics.overallTrustScore}/100 trust</span>
      </div>
      <div className="analytics-grid">
        <TrustBreakdown overallTrustScore={analytics.overallTrustScore} items={analytics.trustBreakdown} />
        <SourceMixList items={analytics.sourceMix} />
      </div>
      <div className="analytics-grid">
        <SnapshotList title="Strongest signals" items={analytics.strongestSignals} />
        <SnapshotList title="Weakest gaps" items={analytics.weakestGaps} />
      </div>
      <TimelineList items={analytics.timeline} />
    </section>
  )
}

function AnalysisRunPanel({
  analytics,
  job,
  matchCandidates,
  supplier,
  latestAssessment,
}: {
  analytics: SupplierAnalytics | null
  job: AnalysisJob
  matchCandidates: SupplierMatchCandidate[]
  supplier: SupplierDetail
  latestAssessment: RiskAssessment | undefined
}) {
  const stages = buildAnalysisStages({ analytics, job, matchCandidates, supplier, latestAssessment })
  const reachableSourceCount = supplier.sourceChecks.filter((sourceCheck) => sourceCheck.status === 'Reachable').length
  const failedSourceCount = supplier.sourceChecks.filter(
    (sourceCheck) => sourceCheck.status === 'Blocked' || sourceCheck.status === 'Failed',
  ).length

  return (
    <section className={`analysis-run-panel analysis-run-${job.status.toLowerCase()}`}>
      <div className="section-heading">
        <p>{isActiveAnalysisJob(job) ? 'Analysis running' : `Analysis ${job.status.toLowerCase()}`}</p>
        <span>{job.progressMessage}</span>
      </div>
      <div className="analysis-run-grid">
        <div className="analysis-stage-list">
          {stages.map((stage) => (
            <div className={`analysis-stage analysis-stage-${stage.status}`} key={stage.label}>
              <span>{formatStageMarker(stage.status)}</span>
              <div>
                <strong>{stage.label}</strong>
                <small>{stage.detail}</small>
              </div>
            </div>
          ))}
        </div>
        <div className="analysis-live-summary">
          <StatusField label="Evidence found" value={`${reachableSourceCount} reachable / ${failedSourceCount} failed`} />
          <StatusField label="Facts extracted" value={`${supplier.supplierFacts.length}`} />
          <StatusField label="Identity candidates" value={`${matchCandidates.length}`} />
          <StatusField label="Trust score" value={analytics ? `${analytics.overallTrustScore}/100` : 'Pending'} />
        </div>
      </div>
      {job.errorMessage && <p className="analysis-run-error">{job.errorMessage}</p>}
    </section>
  )
}

function SourceMixList({ items }: { items: SourceMixItem[] }) {
  const maxCount = Math.max(1, ...items.map((item) => item.count))

  return (
    <div className="source-mix-panel">
      <div className="trust-score-header">
        <strong>Source mix</strong>
        <span>{items.reduce((total, item) => total + item.count, 0)} records</span>
      </div>
      <div className="source-mix-list">
        {items.map((item) => (
          <div className="source-mix-row" key={item.label}>
            <div>
              <strong>{item.label}</strong>
              <span>{item.status}</span>
            </div>
            <div className="trust-bar-track">
              <span style={{ width: `${(item.count / maxCount) * 100}%` }} />
            </div>
            <strong>{item.count}</strong>
          </div>
        ))}
      </div>
    </div>
  )
}

function TimelineList({ items }: { items: TimelineItem[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <div className="timeline-panel">
      <strong>Review timeline</strong>
      <div className="timeline-list">
        {items.map((item) => (
          <article className="timeline-item" key={`${item.occurredAt}-${item.type}-${item.title}`}>
            <span>{formatShortDate(item.occurredAt)}</span>
            <div>
              <strong>{item.title}</strong>
              <p>{item.description}</p>
            </div>
            <small>{item.status}</small>
          </article>
        ))}
      </div>
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

function MatchCandidateList({
  candidates,
  isLoading,
  reviewingCandidateId,
  onConfirm,
  onReject,
}: {
  candidates: SupplierMatchCandidate[]
  isLoading: boolean
  reviewingCandidateId: number | null
  onConfirm: (candidateId: number) => void
  onReject: (candidateId: number) => void
}) {
  if (isLoading && candidates.length === 0) {
    return <p className="muted evidence-empty">Finding possible supplier identities...</p>
  }

  if (candidates.length === 0) {
    return <p className="muted evidence-empty">No match candidates proposed yet.</p>
  }

  return (
    <div className="match-candidate-list">
      {[...candidates]
        .sort(compareMatchCandidates)
        .map((candidate) => {
          const isReviewing = reviewingCandidateId === candidate.id
          const isTerminal = candidate.status === 'Confirmed' || candidate.status === 'Rejected'

          return (
            <article className={`match-candidate match-${candidate.status.toLowerCase()}`} key={candidate.id}>
              <div className="match-candidate-title">
                <div>
                  <strong>{formatCandidateDisplayName(candidate.candidateName)}</strong>
                  <span>{formatCandidateLocation(candidate)}</span>
                </div>
                <span className="match-confidence">
                  {formatMatchConfidence(candidate.confidenceScore)} · {candidate.confidenceScore}/100
                </span>
              </div>
              <p>{candidate.matchReason}</p>
              <div className="match-candidate-meta">
                <span>{formatCandidateStatus(candidate.status)}</span>
                {candidate.sourceName && <span>{candidate.sourceName}</span>}
                {candidate.websiteUrl && (
                  <a href={candidate.websiteUrl} rel="noreferrer" target="_blank">
                    Website
                  </a>
                )}
                {candidate.sourceUrl && candidate.sourceUrl !== candidate.websiteUrl && (
                  <a href={candidate.sourceUrl} rel="noreferrer" target="_blank">
                    Source
                  </a>
                )}
              </div>
              <div className="match-candidate-actions">
                <button
                  className="primary"
                  disabled={isReviewing || candidate.status === 'Confirmed'}
                  onClick={() => onConfirm(candidate.id)}
                  type="button"
                >
                  {candidate.status === 'Confirmed' ? 'Saved identity' : isReviewing ? 'Saving...' : 'Save as identity'}
                </button>
                <button
                  className="ghost"
                  disabled={isReviewing || isTerminal}
                  onClick={() => onReject(candidate.id)}
                  type="button"
                >
                  Dismiss
                </button>
              </div>
            </article>
          )
        })}
    </div>
  )
}

function isActiveAnalysisJob(job: AnalysisJob | null | undefined) {
  return job?.status === 'Queued' || job?.status === 'Running'
}

function findActiveOrLatestAnalysisJob(jobs: AnalysisJob[]) {
  const sortedJobs = [...safeArray(jobs)].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))

  return sortedJobs.find(isActiveAnalysisJob) ?? sortedJobs[0] ?? null
}

type AnalysisStageStatus = 'done' | 'active' | 'pending' | 'failed'

type AnalysisStage = {
  label: string
  detail: string
  status: AnalysisStageStatus
}

function buildAnalysisStages({
  analytics,
  job,
  matchCandidates,
  supplier,
  latestAssessment,
}: {
  analytics: SupplierAnalytics | null
  job: AnalysisJob
  matchCandidates: SupplierMatchCandidate[]
  supplier: SupplierDetail
  latestAssessment: RiskAssessment | undefined
}): AnalysisStage[] {
  const progress = job.progressMessage.toLowerCase()
  const isFailed = job.status === 'Failed'
  const isCompleted = job.status === 'Completed'
  const reachableSourceCount = supplier.sourceChecks.filter((sourceCheck) => sourceCheck.status === 'Reachable').length
  const websiteChecked = supplier.sourceChecks.some((sourceCheck) =>
    sourceCheck.sourceName.toLowerCase().includes('website'),
  )
  const factsExtracted = supplier.supplierFacts.length > 0
  const riskMemoReady = Boolean(latestAssessment)
  const analyticsReady = Boolean(analytics)

  return [
    {
      label: 'Queued',
      detail: formatAnalysisJobTime(job.createdAt),
      status: stageStatus({ done: job.status !== 'Queued', active: job.status === 'Queued', isFailed }),
    },
    {
      label: 'Website checked',
      detail: websiteChecked ? 'Website evidence stored' : 'Skipped if no website exists',
      status: stageStatus({
        done: websiteChecked || isCompleted,
        active: progress.includes('website'),
        isFailed,
      }),
    },
    {
      label: 'Public evidence',
      detail: reachableSourceCount > 0 ? `${reachableSourceCount} reachable source${reachableSourceCount === 1 ? '' : 's'}` : 'Searching sources',
      status: stageStatus({
        done: reachableSourceCount > 0 || isCompleted,
        active: progress.includes('search') || progress.includes('evidence'),
        isFailed,
      }),
    },
    {
      label: 'Facts extracted',
      detail: factsExtracted ? `${supplier.supplierFacts.length} fact${supplier.supplierFacts.length === 1 ? '' : 's'} stored` : 'Waiting for source text',
      status: stageStatus({
        done: factsExtracted || isCompleted,
        active: progress.includes('facts'),
        isFailed,
      }),
    },
    {
      label: 'Risk memo',
      detail: latestAssessment ? `${latestAssessment.riskLevel} / ${formatRiskAssessmentScore(latestAssessment)}` : 'Not generated yet',
      status: stageStatus({
        done: riskMemoReady || isCompleted,
        active: progress.includes('risk memo'),
        isFailed,
      }),
    },
    {
      label: 'Analytics refreshed',
      detail: analytics ? `${analytics.overallTrustScore}/100 trust score` : 'Waiting for analysis data',
      status: stageStatus({
        done: analyticsReady && isCompleted,
        active: progress.includes('analytics'),
        isFailed,
      }),
    },
    {
      label: 'Identity review',
      detail: matchCandidates.length > 0 ? `${matchCandidates.length} candidate${matchCandidates.length === 1 ? '' : 's'} available` : 'Can be reviewed after evidence',
      status: stageStatus({
        done: matchCandidates.some((candidate) => candidate.status === 'Confirmed'),
        active: matchCandidates.some((candidate) => candidate.status === 'Proposed'),
        isFailed,
      }),
    },
  ]
}

function stageStatus({
  done,
  active,
  isFailed,
}: {
  done: boolean
  active: boolean
  isFailed: boolean
}): AnalysisStageStatus {
  if (done) {
    return 'done'
  }

  if (isFailed) {
    return 'failed'
  }

  return active ? 'active' : 'pending'
}

function formatStageMarker(status: AnalysisStageStatus) {
  if (status === 'done') {
    return 'OK'
  }

  if (status === 'active') {
    return '...'
  }

  if (status === 'failed') {
    return 'ERR'
  }

  return '--'
}

function formatAnalysisJobTime(value: string) {
  return `Started ${formatShortDate(value)}`
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

function buildVisibleMatchCandidates(candidates: SupplierMatchCandidate[]) {
  const visible = new Map<string, SupplierMatchCandidate>()

  for (const candidate of candidates) {
    if (candidate.status === 'Rejected') {
      continue
    }

    const key = [
      normalizeIdentityPart(formatCandidateDisplayName(candidate.candidateName)),
      candidate.countryCode?.trim().toUpperCase() ?? '',
    ].join('|')
    const existing = visible.get(key)

    if (!existing || compareMatchCandidates(candidate, existing) < 0) {
      visible.set(key, candidate)
    }
  }

  return [...visible.values()].sort(compareMatchCandidates)
}

function buildKnownInformation({
  supplier,
  confirmedIdentity,
  latestAssessment,
  latestEvidenceQuality,
  reachableSourceCount,
}: {
  supplier: SupplierDetail
  confirmedIdentity: SupplierMatchCandidate | null
  latestAssessment: RiskAssessment | undefined
  latestEvidenceQuality: EvidenceQuality | null
  reachableSourceCount: number
}) {
  const verifiedCertifications = supplier.certifications.filter((certification) => certification.isVerified)
  const items = [
    confirmedIdentity
      ? `Confirmed identity: ${formatCandidateDisplayName(confirmedIdentity.candidateName)}`
      : `Supplier record: ${supplier.name} in ${supplier.countryCode} for ${supplier.industry}`,
  ]

  if (supplier.websiteUrl || confirmedIdentity?.websiteUrl) {
    items.push(`Website available: ${readHostname(supplier.websiteUrl ?? confirmedIdentity?.websiteUrl ?? null)}`)
  }

  if (latestEvidenceQuality?.hasReachableRegistrySource) {
    items.push('Registry evidence found in public sources')
  } else {
    items.push('Legal registration evidence still needs confirmation')
  }

  items.push(
    verifiedCertifications.length > 0
      ? `Verified certifications: ${verifiedCertifications.map((certification) => certification.standard).join(', ')}`
      : 'No verified certification saved yet',
  )

  items.push(
    reachableSourceCount > 0
      ? `${reachableSourceCount} reachable public evidence source${reachableSourceCount === 1 ? '' : 's'}`
      : 'No reachable public evidence source yet',
  )

  if (latestAssessment) {
    items.push(`Current risk view: ${latestAssessment.riskLevel}`)
  }

  return items
}

function buildRiskMemoItems({
  hasConfirmedIdentity,
  latestAssessment,
  latestEvidenceQuality,
  nextEvidenceItems,
}: {
  hasConfirmedIdentity: boolean
  latestAssessment: RiskAssessment | undefined
  latestEvidenceQuality: EvidenceQuality | null
  nextEvidenceItems: string[]
}) {
  const items = [
    hasConfirmedIdentity
      ? 'The supplier identity has been saved, so evidence can be interpreted against a selected entity.'
      : 'The supplier identity is not saved yet, so risk is provisional.',
  ]

  if (latestAssessment) {
    items.push(`Latest stored risk decision: ${latestAssessment.riskLevel} with ${formatRiskAssessmentScore(latestAssessment).toLowerCase()}.`)
  } else {
    items.push('No stored risk decision exists yet.')
  }

  if (latestEvidenceQuality) {
    items.push(`Evidence quality is ${latestEvidenceQuality.band.toLowerCase()} at ${latestEvidenceQuality.score}/100.`)
  }

  if (nextEvidenceItems.length > 0) {
    items.push(`Main blocker: ${nextEvidenceItems[0]}`)
  }

  return items
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

function formatShortDate(value: string) {
  const timestamp = Date.parse(value)

  if (Number.isNaN(timestamp)) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(timestamp))
}

function formatRiskAssessmentScore(assessment: RiskAssessment | undefined) {
  if (!assessment) {
    return 'No score yet'
  }

  if (assessment.riskLevel === 'Unknown') {
    return 'Not scored'
  }

  return `${assessment.score}/100`
}

function buildNextAction({
  hasConfirmedIdentity,
  loadingMatchCandidates,
  matchCandidateCount,
  nextEvidenceCount,
  latestAssessment,
}: {
  hasConfirmedIdentity: boolean
  loadingMatchCandidates: boolean
  matchCandidateCount: number
  nextEvidenceCount: number
  latestAssessment: RiskAssessment | undefined
}): {
  title: string
  description: string
  buttonLabel: string
  status: string
  step: ReviewStep
} {
  if (!hasConfirmedIdentity) {
    return {
      title: 'Confirm supplier identity',
      description: matchCandidateCount > 0
        ? 'Review the proposed legal entities and confirm the one this supplier record should represent.'
        : 'Find possible legal entities before trusting evidence, risk, or reports.',
      buttonLabel: loadingMatchCandidates ? 'Finding matches...' : matchCandidateCount > 0 ? 'Review matches' : 'Find matches',
      status: 'Identity blocker',
      step: 'identity',
    }
  }

  if (nextEvidenceCount > 0) {
    return {
      title: 'Close evidence gaps',
      description: 'The identity is confirmed, but important verification gaps still need review.',
      buttonLabel: 'Review evidence',
      status: `${nextEvidenceCount} open gaps`,
      step: 'evidence',
    }
  }

  if (!latestAssessment) {
    return {
      title: 'Review risk',
      description: 'Evidence is available, but no risk decision has been stored for this supplier yet.',
      buttonLabel: 'Open risk',
      status: 'Risk pending',
      step: 'risk',
    }
  }

  return {
    title: 'Prepare report',
    description: 'The main review inputs are available. Check recommendations before exporting a report.',
    buttonLabel: 'Open report',
    status: 'Ready',
    step: 'report',
  }
}

function summarizeMatchCandidates(candidates: SupplierMatchCandidate[]) {
  const confirmed = candidates.filter((candidate) => candidate.status === 'Confirmed').length
  const proposed = candidates.filter((candidate) => candidate.status === 'Proposed').length

  if (confirmed > 0) {
    return `${confirmed} confirmed`
  }

  if (proposed > 0) {
    return `${proposed} proposed`
  }

  return `${candidates.length} candidates`
}

function compareMatchCandidates(a: SupplierMatchCandidate, b: SupplierMatchCandidate) {
  return matchStatusRank(b.status) - matchStatusRank(a.status) ||
    b.confidenceScore - a.confidenceScore ||
    Date.parse(b.createdAt) - Date.parse(a.createdAt)
}

function matchStatusRank(status: SupplierMatchCandidate['status']) {
  if (status === 'Confirmed') {
    return 3
  }

  if (status === 'Proposed') {
    return 2
  }

  return 1
}

function formatCandidateLocation(candidate: SupplierMatchCandidate) {
  return [candidate.countryCode, readHostname(candidate.websiteUrl)]
    .filter(Boolean)
    .join(' · ') || 'No location details'
}

function formatCandidateDisplayName(value: string) {
  const normalized = value
    .replace(/\*\*/g, '')
    .replace(/\s+/g, ' ')
    .trim()

  const companyNumberIndex = normalized.search(/\s+\(company\s+number/i)
  if (companyNumberIndex > 0) {
    return normalized.slice(0, companyNumberIndex).trim()
  }

  const sentenceMarker = normalized.search(/\s+(is|operates|appears|runs|speciali[sz]es)\s+/i)
  if (sentenceMarker > 0 && sentenceMarker <= 80) {
    return normalized.slice(0, sentenceMarker).trim()
  }

  return shortenText(normalized, 90)
}

function readHostname(url: string | null) {
  if (!url) {
    return null
  }

  try {
    return new URL(url).hostname
  } catch {
    return url
  }
}

function formatMatchConfidence(score: number) {
  if (score >= 75) {
    return 'Strong match'
  }

  if (score >= 50) {
    return 'Plausible match'
  }

  return 'Weak match'
}

function formatCandidateStatus(status: SupplierMatchCandidate['status']) {
  if (status === 'Confirmed') {
    return 'Saved'
  }

  if (status === 'Rejected') {
    return 'Dismissed'
  }

  return 'Needs decision'
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
    items.push('Company registration evidence not verified.')
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
