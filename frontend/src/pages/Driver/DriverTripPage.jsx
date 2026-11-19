import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import useTripPayment from '../../hooks/useTripPayment';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';
import TripDetails from '../../components/TripDetails';
import { money } from '../../utils/format';
import { rememberTrip } from '../../utils/recentTrips';

const PROGRESS_ACTIONS = {
  DriverAccepted: ['en-route', 'Bắt đầu đến đón (EnRoute)'],
  EnRoute: ['pickup', 'Đã đón khách (PickedUp)'],
  PickedUp: ['dropoff', 'Đã trả khách & Thanh toán (DroppedOff)'],
};

export default function DriverTripPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user } = useAuth();
  const load = useCallback(signal => tripApi.getById(id, signal), [id]);
  const { data: trip, loading, error, refresh } = usePolling(load);
  const [submitting, setSubmitting] = useState(false);
  const locked = useRef(false);
  const [actionError, setActionError] = useState('');

  const { data: payment, error: paymentError, refresh: refreshPayment } = useTripPayment(id, trip?.status);

  useEffect(() => {
    if (trip && user?.id) {
      rememberTrip(user.id, trip);
    }
  }, [trip, user?.id]);

  const progressAction = trip?.driver?.id === user.id ? PROGRESS_ACTIONS[trip.status] : null;

  const runDriverAction = async actionName => {
    if (locked.current) return;
    locked.current = true;
    setSubmitting(true);
    setActionError('');
    try {
      await tripApi.driverAction(id, actionName);
      if (actionName === 'driver-reject') {
        navigate('/driver');
        return;
      }
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      await refresh();
      await refreshPayment();
      locked.current = false;
      setSubmitting(false);
    }
  };

  return (
    <main>
      <h1>Chuyến của tài xế</h1>
      <p>Mã chuyến: <code>{id}</code></p>
      {loading && <p>Đang tải...</p>}
      {error && <p role="alert">{error}</p>}
      {actionError && <p role="alert">{actionError}</p>}

      {trip && (
        <>
          {trip.status === 'PaymentPending' && (
            <fieldset>
              <legend>Đang xử lý thanh toán (PaymentPending)</legend>
              <p>Chuyến đã kết thúc, hệ thống đang xử lý thanh toán qua Saga.</p>
              {payment?.status === 'PENDING' && (
                <p>Giao dịch cổng thanh toán đang ở trạng thái <code>PENDING</code> (chờ đối soát).</p>
              )}
            </fieldset>
          )}

          {trip.status === 'Paid' && payment?.status !== 'REFUNDED' && (
            <fieldset>
              <legend>Kết quả thanh toán (Paid)</legend>
              {payment && payment.status === 'SUCCEEDED' ? (
                <>
                  <p>Tổng cước chuyến đi: <strong>{money(payment.amount)}</strong></p>
                  <p>Số tiền thực nhận của tài xế: <strong>{money(payment.driverAmount)}</strong></p>
                  <p>Phí nền tảng: {money(payment.platformAmount)}</p>
                  <p>
                    Phương thức thanh toán:{' '}
                    <strong>{payment.method === 'WALLET' ? 'Ví (WALLET)' : 'Thẻ giả lập (CARD)'}</strong>
                  </p>
                  <p>Mã giao dịch (Payment ID): <code>{payment.id}</code></p>
                </>
              ) : (
                <p>{paymentError || 'Đang tải chi tiết tiền nhận...'}</p>
              )}
            </fieldset>
          )}

          {trip.status === 'PaymentFailed' && (
            <fieldset>
              <legend>Thanh toán thất bại (PaymentFailed)</legend>
              <p>Khách thanh toán chưa thành công; chưa phát sinh khoản thu cho chuyến này.</p>
              {payment?.failureReason === 'INSUFFICIENT_FUNDS' && (
                <p>Lý do: Ví của khách không đủ tiền (<code>INSUFFICIENT_FUNDS</code>).</p>
              )}
              {payment?.failureReason === 'CARD_DECLINED' && (
                <p>Lý do: Thẻ của khách bị từ chối (<code>CARD_DECLINED</code>).</p>
              )}
              {payment?.failureReason && !['INSUFFICIENT_FUNDS', 'CARD_DECLINED'].includes(payment.failureReason) && (
                <p>Lý do: <code>{payment.failureReason}</code></p>
              )}
            </fieldset>
          )}

          {trip.status === 'RefundPending' && (
            <fieldset>
              <legend>Đang xử lý hoàn tiền (RefundPending)</legend>
              <p>Yêu cầu bồi thường đã được Admin phê duyệt, đang chờ Payment Service hoàn tiền.</p>
              <p>Mã tham chiếu chuyến: <code>{trip.id}</code></p>
              {payment?.id && <p>Mã giao dịch gốc: <code>{payment.id}</code></p>}
            </fieldset>
          )}

          {(trip.status === 'Refunded' || payment?.status === 'REFUNDED') && (
            <fieldset>
              <legend>Đã hoàn tiền (Refunded)</legend>
              <p>Giao dịch đã hoàn tiền. Khoản nhận của chuyến ({money(payment?.driverAmount ?? 0)}) đã được thu hồi trong sổ cái.</p>
              {payment?.id && <p>Mã giao dịch: <code>{payment.id}</code></p>}
            </fieldset>
          )}

          {trip.status === 'RefundNeedsAttention' && (
            <fieldset>
              <legend>Hoàn tiền cần hỗ trợ xử lý (RefundNeedsAttention)</legend>
              <p>Lệnh hoàn tiền bị từ chối xác định (ví dụ số dư tài khoản nhận không đủ để thu hồi). Chờ bộ phận hỗ trợ xử lý.</p>
              <p>Mã tham chiếu chuyến: <code>{trip.id}</code></p>
            </fieldset>
          )}

          <TripDetails trip={trip} />

          {trip.status === 'DriverOffered' && (
            <fieldset disabled={submitting || !!error}>
              <legend>Phản hồi lời mời chuyến</legend>
              <button type="button" onClick={() => runDriverAction('driver-accept')}>
                {submitting ? 'Đang xử lý...' : 'Nhận chuyến'}
              </button>{' '}
              <button type="button" onClick={() => runDriverAction('driver-reject')}>
                Từ chối chuyến
              </button>
            </fieldset>
          )}

          {progressAction && (
            <p>
              <button
                type="button"
                disabled={submitting || !!error}
                onClick={() => runDriverAction(progressAction[0])}
              >
                {submitting ? 'Đang xử lý...' : progressAction[1]}
              </button>
            </p>
          )}
        </>
      )}

      <p>
        <button type="button" disabled={submitting} onClick={() => { refresh(); refreshPayment(); }}>
          Tải lại
        </button>
      </p>
      <p>
        <Link to="/wallet">Ví của tôi</Link>
        {' | '}
        <Link to="/driver">Về trang Driver</Link>
      </p>
    </main>
  );
}
