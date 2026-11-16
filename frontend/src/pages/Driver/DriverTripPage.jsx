import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { paymentApi } from '../../api/paymentApi';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';
import TripDetails from '../../components/TripDetails';

const actions = {
  DriverAccepted: ['en-route', 'Bắt đầu đến đón'],
  EnRoute: ['pickup', 'Đã đón khách'],
  PickedUp: ['dropoff', 'Đã trả khách'],
};

const formatCurrency = amount =>
  new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(amount ?? 0);

export default function DriverTripPage() {
  const { id } = useParams();
  const { user } = useAuth();
  const load = useCallback(signal => tripApi.getById(id, signal), [id]);
  const { data: trip, loading, error, refresh } = usePolling(load);
  const [submitting, setSubmitting] = useState(false);
  const locked = useRef(false);
  const [actionError, setActionError] = useState('');

  const [payment, setPayment] = useState(null);
  const [paymentError, setPaymentError] = useState('');

  const action = trip?.driver?.id === user.id ? actions[trip.status] : null;

  useEffect(() => {
    if (!trip || (trip.status !== 'Paid' && trip.status !== 'PaymentFailed')) {
      setPayment(null);
      setPaymentError('');
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        const res = await paymentApi.getByTripId(id);
        if (!cancelled) {
          setPayment(res);
          setPaymentError('');
        }
      } catch (err) {
        if (!cancelled) {
          setPayment(null);
          setPaymentError(getErrorMessage(err));
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [id, trip?.status]);

  const submit = async () => {
    if (!action || locked.current) return;
    locked.current = true;
    setSubmitting(true);
    setActionError('');
    try {
      await tripApi.driverAction(id, action[0]);
    } catch (err) {
      setActionError(getErrorMessage(err));
    } finally {
      await refresh();
      locked.current = false;
      setSubmitting(false);
    }
  };

  return (
    <main>
      <h1>Chuyến của tài xế</h1>
      <p>Mã chuyến: {id}</p>
      {loading && <p>Đang tải...</p>}
      {error && <p role="alert">{error}</p>}
      {actionError && <p role="alert">{actionError}</p>}
      {trip && (
        <>
          {trip.status === 'Paid' && (
            <fieldset style={{ borderColor: 'green', marginBottom: '1rem' }}>
              <legend style={{ color: 'green', fontWeight: 'bold' }}>
                Đã nhận thanh toán (Paid)
              </legend>
              {payment && payment.status === 'SUCCEEDED' ? (
                <>
                  <p>
                    Số tiền thực nhận: <strong>{formatCurrency(payment.driverAmount)}</strong>
                  </p>
                  <p>
                    Phương thức thanh toán:{' '}
                    <strong>{payment.method === 'WALLET' ? 'Ví' : 'Thẻ'}</strong>
                  </p>
                </>
              ) : (
                <p>{paymentError || 'Đang tải chi tiết tiền nhận...'}</p>
              )}
            </fieldset>
          )}

          {trip.status === 'PaymentFailed' && (
            <fieldset style={{ borderColor: 'red', marginBottom: '1rem' }}>
              <legend style={{ color: 'red', fontWeight: 'bold' }}>
                Thanh toán thất bại (PaymentFailed)
              </legend>
              <p>Khách thanh toán chưa thành công. Bạn chưa nhận được tiền cho chuyến này.</p>
            </fieldset>
          )}

          <TripDetails trip={trip} />
          <p>API hiện chưa cung cấp tên và số điện thoại khách.</p>
          {action && (
            <button type="button" disabled={submitting || !!error} onClick={submit}>
              {submitting ? 'Đang xử lý...' : action[1]}
            </button>
          )}
          {trip.status === 'DriverOffered' && (
            <p>Hãy về danh sách lời mời để nhận hoặc từ chối chuyến.</p>
          )}
          {trip.status === 'PaymentPending' && <p>Đã trả khách. Chuyến đang chờ thanh toán.</p>}
        </>
      )}
      <p>
        <button type="button" disabled={submitting} onClick={refresh}>
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