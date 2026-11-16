import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { paymentApi } from '../../api/paymentApi';
import { getErrorMessage } from '../../api/client';
import usePolling from '../../hooks/usePolling';
import TripDetails from '../../components/TripDetails';
import { money } from '../../utils/format';

const PAYMENT_METHOD_LABEL = {
  WALLET: 'Ví',
  CARD: 'Thẻ',
};

export default function TripPage() {
  const { id } = useParams();
  const load = useCallback(signal => tripApi.getById(id, signal), [id]);
  const { data: trip, error, loading, refresh } = usePolling(load);
  const [paymentMethod, setPaymentMethod] = useState('WALLET');
  const [actionError, setActionError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [paymentInfo, setPaymentInfo] = useState(null);
  const [paymentLoading, setPaymentLoading] = useState(false);
  const [paymentError, setPaymentError] = useState('');
  const locked = useRef(false);

  useEffect(() => {
    if (!trip || (trip.status !== 'Paid' && trip.status !== 'PaymentFailed')) {
      setPaymentInfo(null);
      setPaymentError('');
      return;
    }

    const controller = new AbortController();
    setPaymentLoading(true);
    setPaymentError('');

    paymentApi.getByTripId(id, controller.signal)
      .then(data => setPaymentInfo(data))
      .catch(err => {
        if (err.name !== 'CanceledError' && err.code !== 'ERR_CANCELED') {
          setPaymentError(getErrorMessage(err));
        }
      })
      .finally(() => setPaymentLoading(false));

    return () => controller.abort();
  }, [id, trip?.status]);

  const confirm = async accept => {
    if (locked.current) return;
    locked.current = true;
    setSubmitting(true);
    setActionError('');
    try { await tripApi.confirm(id, accept, accept ? paymentMethod : null); }
    catch (err) { setActionError(getErrorMessage(err)); }
    finally {
      await refresh();
      locked.current = false;
      setSubmitting(false);
    }
  };

  const paidAmount = paymentInfo?.amount ?? trip?.fare?.price;
  const rawMethod = paymentInfo?.method ?? paymentInfo?.paymentMethod ?? trip?.fare?.paymentMethod;
  const methodDisplay = PAYMENT_METHOD_LABEL[rawMethod] || rawMethod || 'Không xác định';

  return <main>
    <h1>Chi tiết chuyến</h1>
    <p>Mã chuyến: {id}</p>
    <p>Thông tin tài xế và lịch sử tự cập nhật mỗi 3 giây.</p>
    {loading && <p>Đang tải chuyến...</p>}
    {error && <p role="alert">{error}</p>}
    {actionError && <p role="alert">{actionError}</p>}

    {trip && <>
      {trip.status === 'Paid' && (
        <fieldset style={{ borderColor: 'green', marginBottom: '1rem' }}>
          <legend style={{ color: 'green', fontWeight: 'bold' }}>Thanh toán thành công (Paid)</legend>
          {paymentLoading && <p>Đang tải chi tiết thanh toán...</p>}
          {paymentError && <p role="alert">{paymentError}</p>}
          <p>Số tiền đã thanh toán: <strong>{paidAmount != null ? money(paidAmount) : '—'}</strong></p>
          <p>Phương thức thanh toán: <strong>{methodDisplay}</strong></p>
        </fieldset>
      )}

      {trip.status === 'PaymentFailed' && (
        <fieldset style={{ borderColor: 'red', marginBottom: '1rem' }}>
          <legend style={{ color: 'red', fontWeight: 'bold' }}>Thanh toán thất bại (PaymentFailed)</legend>
          {paymentLoading && <p>Đang tải nguyên nhân lỗi thanh toán...</p>}
          {paymentError && <p role="alert">{paymentError}</p>}
          {!paymentLoading && (
            <p style={{ color: 'red' }}>
              Lý do thất bại (failureReason):{' '}
              <strong>{paymentInfo?.failureReason || 'Không có thông tin chi tiết lỗi từ cổng thanh toán.'}</strong>
            </p>
          )}
        </fieldset>
      )}

      <TripDetails trip={trip} />

      {trip.status === 'Quoted' && trip.fare?.status === 'Pending' && <fieldset disabled={submitting || !!error}>
        <legend>Xác nhận báo giá</legend>
        <label htmlFor="payment-method">Thanh toán </label>
        <select id="payment-method" value={paymentMethod} onChange={e => setPaymentMethod(e.target.value)}>
          <option value="WALLET">Ví</option>
          <option value="CARD">Thẻ</option>
        </select>{' '}
        <button type="button" onClick={() => confirm(true)}>Đồng ý</button>{' '}
        <button type="button" onClick={() => confirm(false)}>Từ chối</button>
      </fieldset>}
      {trip.status === 'DriverOffered' && <p>Đang chờ tài xế nhận chuyến.</p>}
      {trip.status === 'Failed' && <p>Không tìm được tài xế phù hợp cho chuyến này.</p>}
      {trip.status === 'PaymentPending' && <p>Chuyến đã trả khách, đang chờ thanh toán.</p>}
    </>}

    <p><button type="button" disabled={submitting} onClick={refresh}>Tải lại</button></p>
    <p>
      <Link to="/wallet">Ví của tôi</Link>{' | '}
      <Link to="/rider" state={{ showHome: true }}>Về trang Rider</Link>
    </p>
  </main>;
}