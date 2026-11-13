import { useCallback, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import usePolling from '../../hooks/usePolling';
import TripDetails from '../../components/TripDetails';

export default function TripPage() {
  const { id } = useParams();
  const load = useCallback(signal => tripApi.getById(id, signal), [id]);
  const { data: trip, error, loading, refresh } = usePolling(load);
  const [paymentMethod, setPaymentMethod] = useState('WALLET');
  const [actionError, setActionError] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const locked = useRef(false);

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

  return <main>
    <h1>Chi tiết chuyến</h1>
    <p>Mã chuyến: {id}</p>
    <p>Thông tin tài xế và lịch sử tự cập nhật mỗi 3 giây.</p>
    {loading && <p>Đang tải chuyến...</p>}
    {error && <p role="alert">{error}</p>}
    {actionError && <p role="alert">{actionError}</p>}
    {trip && <>
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
    <p><Link to="/rider" state={{ showHome: true }}>Về trang Rider</Link></p>
  </main>;
}
