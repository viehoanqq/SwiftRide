import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { paymentApi } from '../../api/paymentApi';
import useTripPayment from '../../hooks/useTripPayment';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';
import TripDetails from '../../components/TripDetails';
import { money } from '../../utils/format';
import { rememberTrip } from '../../utils/recentTrips';

const PAYMENT_METHOD_LABEL = {
  WALLET: 'Ví (WALLET)',
  CARD: 'Thẻ giả lập (CARD)',
};

const CARD_TOKEN_OPTIONS = [
  {
    value: 'test-card-approved',
    label: 'test-card-approved — Cổng thẻ chấp nhận (Paid / SUCCEEDED)',
  },
  {
    value: 'test-card-declined',
    label: 'test-card-declined — Thẻ bị từ chối (PaymentFailed / CARD_DECLINED)',
  },
  {
    value: 'test-card-unknown',
    label: 'test-card-unknown — Chưa rõ kết quả, treo chờ đối soát (PaymentPending / PENDING)',
  },
  {
    value: 'custom',
    label: '-- Tự nhập CardToken khác --',
  },
];

export default function TripPage() {
  const { id } = useParams();
  const { user } = useAuth();
  const load = useCallback(signal => tripApi.getById(id, signal), [id]);
  const { data: trip, error, loading, refresh } = usePolling(load);

  const [paymentMethod, setPaymentMethod] = useState('WALLET');
  const [cardTokenOption, setCardTokenOption] = useState(CARD_TOKEN_OPTIONS[0].value);
  const [customCardToken, setCustomCardToken] = useState('');
  const [actionError, setActionError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const { data: paymentInfo, loading: paymentLoading, error: paymentError, refresh: refreshPayment } =
    useTripPayment(id, trip?.status);

  const [directPayment, setDirectPayment] = useState(null);
  const [directPaymentError, setDirectPaymentError] = useState('');
  const [directPaymentLoading, setDirectPaymentLoading] = useState(false);

  const locked = useRef(false);

  useEffect(() => {
    if (trip && user?.id) {
      rememberTrip(user.id, trip);
    }
  }, [trip, user?.id]);

  const confirm = async accept => {
    if (locked.current) return;
    setActionError('');

    let resolvedCardToken = null;
    if (accept && paymentMethod === 'CARD') {
      resolvedCardToken = cardTokenOption === 'custom'
        ? customCardToken.trim()
        : cardTokenOption;
      if (!resolvedCardToken) {
        setActionError('Vui lòng chọn hoặc nhập CardToken khi thanh toán bằng Thẻ (CARD).');
        return;
      }
    }

    locked.current = true;
    setSubmitting(true);
    try {
      await tripApi.confirm(id, accept, accept ? paymentMethod : null, resolvedCardToken);
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      await refresh();
      locked.current = false;
      setSubmitting(false);
    }
  };

  const handleLoadPaymentById = async paymentId => {
    if (!paymentId) return;
    setDirectPaymentLoading(true);
    setDirectPaymentError('');
    try {
      const detail = await paymentApi.getById(paymentId);
      setDirectPayment(detail);
    } catch (err) {
      setDirectPaymentError(getErrorMessage(err));
    } finally {
      setDirectPaymentLoading(false);
    }
  };

  const paidAmount = paymentInfo?.amount ?? trip?.fare?.price;
  const rawMethod = paymentInfo?.method ?? paymentInfo?.paymentMethod ?? trip?.fare?.paymentMethod;
  const methodDisplay = PAYMENT_METHOD_LABEL[rawMethod] || rawMethod || 'Không xác định';

  return (
    <main>
      <h1>Chi tiết chuyến</h1>
      <p>Mã chuyến: <code>{id}</code></p>
      <p>Thông tin tài xế, thanh toán và lịch sử tự cập nhật mỗi 3 giây.</p>
      {loading && <p>Đang tải chuyến...</p>}
      {error && <p role="alert">{error}</p>}
      {actionError && <p role="alert">{actionError}</p>}

      {trip && (
        <>
          {trip.status === 'PaymentPending' && (
            <fieldset>
              <legend>Đang xử lý thanh toán (PaymentPending)</legend>
              <p>Chuyến đã kết thúc, hệ thống đang xử lý thanh toán (Saga qua RabbitMQ).</p>
              {paymentInfo?.status === 'PENDING' && (
                <p>
                  Trạng thái Payment: <code>PENDING</code> (chưa có kết quả cuối từ cổng thẻ giả lập, đang chờ đối soát).
                </p>
              )}
              {paymentInfo?.id && <p>Mã tham chiếu Payment: <code>{paymentInfo.id}</code></p>}
            </fieldset>
          )}

          {trip.status === 'Paid' && paymentInfo?.status !== 'REFUNDED' && (
            <fieldset>
              <legend>Đã thanh toán (Paid)</legend>
              {paymentLoading && <p>Đang tải chi tiết thanh toán...</p>}
              {paymentError && <p role="alert">{paymentError}</p>}
              <p>Số tiền đã thanh toán: <strong>{paidAmount != null ? money(paidAmount) : '—'}</strong></p>
              <p>Phương thức thanh toán: <strong>{methodDisplay}</strong></p>
              {paymentInfo && (
                <>
                  <p>Mã giao dịch (Payment ID): <code>{paymentInfo.id}</code></p>
                  <p>Trạng thái giao dịch: <code>{paymentInfo.status}</code> (Số lần thử: {paymentInfo.attemptCount})</p>
                  <p>
                    <button
                      type="button"
                      disabled={directPaymentLoading}
                      onClick={() => handleLoadPaymentById(paymentInfo.id)}
                    >
                      {directPaymentLoading ? 'Đang tra cứu...' : 'Tra cứu biên lai theo Payment ID (GET /payments/{id})'}
                    </button>
                  </p>
                </>
              )}
            </fieldset>
          )}

          {trip.status === 'PaymentFailed' && (
            <fieldset>
              <legend>Thanh toán thất bại (PaymentFailed)</legend>
              {paymentLoading && <p>Đang tải nguyên nhân lỗi thanh toán...</p>}
              {paymentError && <p role="alert">{paymentError}</p>}
              <p>Thanh toán thất bại; tài khoản của bạn chưa bị trừ tiền.</p>
              {!paymentLoading && (
                <p>
                  Lý do thất bại (failureReason):{' '}
                  <strong>
                    {paymentInfo?.failureReason === 'INSUFFICIENT_FUNDS'
                      ? 'Ví không đủ tiền để thanh toán chuyến đi (INSUFFICIENT_FUNDS).'
                      : paymentInfo?.failureReason === 'CARD_DECLINED'
                        ? 'Thẻ bị từ chối thanh toán (CARD_DECLINED).'
                        : paymentInfo?.failureReason || 'Không có thông tin chi tiết lỗi từ cổng thanh toán.'}
                  </strong>
                </p>
              )}
              {paymentInfo?.id && <p>Mã giao dịch: <code>{paymentInfo.id}</code></p>}
            </fieldset>
          )}

          {trip.status === 'RefundPending' && (
            <fieldset>
              <legend>Đang xử lý hoàn tiền (RefundPending)</legend>
              <p>Yêu cầu bồi thường đã được phê duyệt, hệ thống đang xử lý hoàn tiền.</p>
              <p>Mã tham chiếu chuyến (Trip ID): <code>{trip.id}</code></p>
              {paymentInfo?.id && <p>Mã tham chiếu giao dịch (Payment ID): <code>{paymentInfo.id}</code></p>}
            </fieldset>
          )}

          {(trip.status === 'Refunded' || paymentInfo?.status === 'REFUNDED') && (
            <fieldset>
              <legend>Đã hoàn tiền (Refunded)</legend>
              <p>
                Giao dịch đã được hoàn tiền thành công:{' '}
                <strong>{money(paymentInfo?.amount ?? trip.fare?.price ?? 0)}</strong>.
              </p>
              <p>Mã tham chiếu chuyến (Trip ID): <code>{trip.id}</code></p>
              {paymentInfo?.id && <p>Mã tham chiếu giao dịch (Payment ID): <code>{paymentInfo.id}</code></p>}
            </fieldset>
          )}

          {trip.status === 'RefundNeedsAttention' && (
            <fieldset>
              <legend>Hoàn tiền cần hỗ trợ xử lý (RefundNeedsAttention)</legend>
              <p>Hoàn tiền chưa thể thực hiện tự động, cần nhân sự hỗ trợ kiểm tra xử lý.</p>
              <p>Mã tham chiếu chuyến (Trip ID): <code>{trip.id}</code></p>
              {paymentInfo?.id && <p>Mã tham chiếu giao dịch (Payment ID): <code>{paymentInfo.id}</code></p>}
            </fieldset>
          )}

          {directPaymentError && <p role="alert">{directPaymentError}</p>}
          {directPayment && (
            <fieldset>
              <legend>Biên lai chi tiết (GET /payments/{directPayment.id})</legend>
              <p>Payment ID: <code>{directPayment.id}</code></p>
              <p>Trip ID: <code>{directPayment.tripId}</code></p>
              <p>Tổng số tiền: <strong>{money(directPayment.amount)}</strong></p>
              <p>Phương thức: <code>{directPayment.method}</code> | Trạng thái: <code>{directPayment.status}</code></p>
              <p>Tài xế nhận: {money(directPayment.driverAmount)} | Phí nền tảng: {money(directPayment.platformAmount)}</p>
              <p>Số lần xử lý (AttemptCount): {directPayment.attemptCount}</p>
              {directPayment.failureReason && <p>Lý do lỗi: <code>{directPayment.failureReason}</code></p>}
            </fieldset>
          )}

          <TripDetails trip={trip} />

          {trip.status === 'Requested' && (
            <div>
              <p role="status">Đang tính giá chuyến đi. Báo giá sẽ tự hiển thị khi có kết quả.</p>
              <button disabled={submitting || !!error} onClick={() => confirm(false)}>Hủy yêu cầu</button>
            </div>
          )}

          {trip.status === 'Quoted' && trip.fare?.status === 'Pending' && (
            <fieldset disabled={submitting || !!error}>
              <legend>Xác nhận báo giá (POST /trips/&#123;id&#125;/confirm)</legend>
              <div>
                <label htmlFor="payment-method">Phương thức thanh toán </label>
                <select
                  id="payment-method"
                  value={paymentMethod}
                  onChange={e => setPaymentMethod(e.target.value)}
                >
                  <option value="WALLET">Ví điện tử (WALLET)</option>
                  <option value="CARD">Thẻ giả lập (CARD)</option>
                </select>
              </div>

              {paymentMethod === 'CARD' && (
                <div>
                  <label htmlFor="card-token-select">Token thẻ giả lập (CardToken) </label>
                  <select
                    id="card-token-select"
                    value={cardTokenOption}
                    onChange={e => setCardTokenOption(e.target.value)}
                  >
                    {CARD_TOKEN_OPTIONS.map(opt => (
                      <option key={opt.value} value={opt.value}>{opt.label}</option>
                    ))}
                  </select>
                  {cardTokenOption === 'custom' && (
                    <div>
                      <label htmlFor="custom-card-token">Nhập CardToken </label>
                      <input
                        id="custom-card-token"
                        type="text"
                        size="32"
                        maxLength={100}
                        placeholder="Ví dụ: test-card-approved"
                        value={customCardToken}
                        onChange={e => setCustomCardToken(e.target.value)}
                      />
                    </div>
                  )}
                </div>
              )}

              <p>
                <button type="button" onClick={() => confirm(true)}>Đồng ý & Tìm tài xế</button>{' '}
                <button type="button" onClick={() => confirm(false)}>Từ chối (Hủy chuyến)</button>
              </p>
            </fieldset>
          )}

          {trip.status === 'DriverOffered' && <p>Đang chờ tài xế phản hồi lời mời nhận chuyến...</p>}
          {trip.status === 'Cancelled' && <p>Bạn đã từ chối báo giá và hủy chuyến đi này.</p>}
          {trip.status === 'Failed' && <p>Không tìm được tài xế phù hợp cho chuyến này.</p>}
        </>
      )}

      <p>
        <button type="button" disabled={submitting} onClick={() => { refresh(); refreshPayment(); }}>
          Tải lại
        </button>
      </p>
      <p>
        <Link to="/rider/book">Đặt chuyến mới</Link>{' | '}
        <Link to="/wallet">Ví của tôi</Link>{' | '}
        <Link to="/rider" state={{ showHome: true }}>Về trang Rider</Link>
      </p>
    </main>
  );
}
