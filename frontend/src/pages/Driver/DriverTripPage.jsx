import { useCallback, useRef, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import usePolling from '../../hooks/usePolling';
import TripDetails from '../../components/TripDetails';

const actions = {
  DriverAccepted: ['en-route', 'Bắt đầu đến đón'],
  EnRoute: ['pickup', 'Đã đón khách'],
  PickedUp: ['dropoff', 'Đã trả khách'],
};

export default function DriverTripPage() {
  const { id } = useParams();
  const { user } = useAuth();
  const load = useCallback(signal => tripApi.getById(id, signal), [id]);
  const { data: trip, loading, error, refresh } = usePolling(load);
  const [submitting, setSubmitting] = useState(false);
  const locked = useRef(false);
  const [actionError, setActionError] = useState('');
  const action = trip?.driver?.id === user.id ? actions[trip.status] : null;

  const submit = async () => {
    if (!action || locked.current) return;
    locked.current = true;
    setSubmitting(true);
    setActionError('');
    try { await tripApi.driverAction(id, action[0]); }
    catch (err) { setActionError(getErrorMessage(err)); }
    finally {
      await refresh();
      locked.current = false;
      setSubmitting(false);
    }
  };

  return <main>
    <h1>Chuyến của tài xế</h1>
    <p>Mã chuyến: {id}</p>
    {loading && <p>Đang tải...</p>}
    {error && <p role="alert">{error}</p>}
    {actionError && <p role="alert">{actionError}</p>}
    {trip && <>
      <TripDetails trip={trip} />
      <p>API hiện chưa cung cấp tên và số điện thoại khách.</p>
      {action && <button type="button" disabled={submitting || !!error} onClick={submit}>
        {submitting ? 'Đang xử lý...' : action[1]}
      </button>}
      {trip.status === 'DriverOffered' && <p>Hãy về danh sách lời mời để nhận hoặc từ chối chuyến.</p>}
      {trip.status === 'PaymentPending' && <p>Đã trả khách. Chuyến đang chờ thanh toán.</p>}
    </>}
    <p><button type="button" disabled={submitting} onClick={refresh}>Tải lại</button></p>
    <Link to="/driver">Về trang Driver</Link>
  </main>;
}
