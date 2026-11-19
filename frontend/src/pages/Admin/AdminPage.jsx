import { useCallback, useEffect, useState } from 'react';
import { authApi } from '../../api/authApi';
import { healthApi } from '../../api/healthApi';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';

const REASON_OPTIONS = [
  {
    value: 'APPROVED_CUSTOMER_REFUND',
    label: 'APPROVED_CUSTOMER_REFUND — Yêu cầu hoàn tiền của khách đã được duyệt',
  },
  {
    value: 'INVALID_TRIP_CHARGE',
    label: 'INVALID_TRIP_CHARGE — Khoản thu chuyến đi không hợp lệ sau đối soát',
  },
];

export default function AdminPage() {
  const { user, logout } = useAuth();
  const [me, setMe] = useState(null);
  const fetchHealth = useCallback(signal => healthApi.check(signal), []);
  const { data: health, loading: healthLoading, error: healthError, refresh: loadHealth } =
    usePolling(fetchHealth, false);

  const [tripId, setTripId] = useState('');
  const [reasonCode, setReasonCode] = useState(REASON_OPTIONS[0].value);
  const [evidenceReference, setEvidenceReference] = useState('case-001');
  const [submitting, setSubmitting] = useState(false);
  const [refundResult, setRefundResult] = useState(null);
  const [refundError, setRefundError] = useState('');

  useEffect(() => {
    authApi.me().then(setMe).catch(() => {});
  }, []);

  const handleApproveRefund = async e => {
    e.preventDefault();
    setRefundError('');
    setRefundResult(null);

    const trimmedTripId = tripId.trim();
    const trimmedEvidence = evidenceReference.trim();
    if (!trimmedTripId) {
      setRefundError('Vui lòng nhập Mã chuyến đi (Trip ID).');
      return;
    }
    if (!trimmedEvidence || trimmedEvidence.length > 200) {
      setRefundError('Mã hồ sơ / bằng chứng bắt buộc và tối đa 200 ký tự.');
      return;
    }

    setSubmitting(true);
    try {
      const res = await tripApi.approveRefund(trimmedTripId, reasonCode, trimmedEvidence);
      setRefundResult({
        tripId: trimmedTripId,
        status: res.status,
        reasonCode,
        evidenceReference: trimmedEvidence,
        at: new Date().toLocaleString('vi-VN'),
      });
    } catch (err) {
      setRefundError(getErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <main>
      <h1>Quản trị viên (Admin) — {user?.name}</h1>
      <p>
        Mã tài khoản: <code>{me?.id || user?.id}</code>
        {me?.phone ? ` | SĐT: ${me.phone}` : ''}
        {' | '}Vai trò: <strong>{me?.role || user?.role}</strong>
      </p>
      <p>
        <button type="button" disabled={healthLoading} onClick={loadHealth}>
          {healthLoading ? 'Đang kiểm tra...' : 'Kiểm tra sức khỏe hệ thống (/health)'}
        </button>{' '}
        <button type="button" onClick={logout}>Đăng xuất</button>
      </p>

      <section>
        <h2>1. Duyệt bồi thường / Hoàn tiền chuyến đi (Refund Saga)</h2>
        <p>
          Chỉ áp dụng cho chuyến đã thanh toán thành công (<code>Paid</code> / Payment <code>SUCCEEDED</code>)
          hoặc cần thử lại bồi thường (<code>RefundNeedsAttention</code> với cùng lý do và mã bằng chứng).
        </p>

        <form onSubmit={handleApproveRefund}>
          <fieldset disabled={submitting}>
            <legend>Form phê duyệt hoàn tiền (POST /trips/&#123;id&#125;/refund-approval)</legend>
            <div>
              <label htmlFor="refund-trip-id">Mã chuyến đi (Trip ID) </label>
              <input
                id="refund-trip-id"
                type="text"
                size="42"
                placeholder="Ví dụ: 5222c0d1-e1e4-496e-8e2b-c882e56f602c"
                value={tripId}
                onChange={e => setTripId(e.target.value)}
                required
              />
            </div>
            <div>
              <label htmlFor="refund-reason">Mã lý do (Reason Code) </label>
              <select
                id="refund-reason"
                value={reasonCode}
                onChange={e => setReasonCode(e.target.value)}
              >
                {REASON_OPTIONS.map(opt => (
                  <option key={opt.value} value={opt.value}>{opt.label}</option>
                ))}
              </select>
            </div>
            <div>
              <label htmlFor="refund-evidence">Mã hồ sơ / Bằng chứng (Evidence Reference) </label>
              <input
                id="refund-evidence"
                type="text"
                size="32"
                maxLength={200}
                placeholder="Ví dụ: case-001"
                value={evidenceReference}
                onChange={e => setEvidenceReference(e.target.value)}
                required
              />
            </div>
            <p>
              <button type="submit" disabled={submitting}>
                {submitting ? 'Đang gửi lệnh hoàn tiền...' : 'Duyệt hoàn tiền'}
              </button>
            </p>
          </fieldset>
        </form>

        {refundError && <p role="alert">Lỗi: {refundError}</p>}

        {refundResult && (
          <fieldset>
            <legend>Kết quả phê duyệt bồi thường</legend>
            <p>Mã chuyến: <code>{refundResult.tripId}</code></p>
            <p>Trạng thái mới của chuyến: <strong>{refundResult.status}</strong></p>
            <p>Lý do: <code>{refundResult.reasonCode}</code></p>
            <p>Hồ sơ bằng chứng: <code>{refundResult.evidenceReference}</code></p>
            <p>Thời điểm duyệt: {refundResult.at}</p>
            <p>
              Hệ thống đã ghi nhận quyết định bồi thường và phát lệnh <code>RefundPayment</code> qua RabbitMQ.
              Rider và Driver có thể mở trang chi tiết chuyến hoặc Ví để xem trạng thái chuyển sang <code>Refunded</code>.
            </p>
          </fieldset>
        )}
      </section>

      <section>
        <h2>2. Trạng thái hệ thống qua API Gateway (/health)</h2>
        {healthError && <p role="alert">{healthError}</p>}
        {health && (
          <>
            <p>Trạng thái tổng thể: <strong>{health.status}</strong></p>
            {health.services?.length > 0 && (
              <table border="1" cellPadding="8" cellSpacing="0">
                <thead>
                  <tr>
                    <th>Microservice</th>
                    <th>Cơ sở dữ liệu</th>
                    <th>Trạng thái</th>
                  </tr>
                </thead>
                <tbody>
                  {health.services.map(svc => (
                    <tr key={svc.service}>
                      <td><code>{svc.service}</code></td>
                      <td>{svc.database || '—'}</td>
                      <td><strong>{svc.status}</strong></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </section>
    </main>
  );
}
