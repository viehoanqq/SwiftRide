import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { tripApi } from '../../api/tripApi';
import { getErrorMessage } from '../../api/client';

export default function TripPage() {
  const { id } = useParams();
  const [trip, setTrip] = useState(null);
  const [paymentMethod, setPaymentMethod] = useState('WALLET');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);

  const refreshTrip = useCallback(async () => {
    try {
      const currentTrip = await tripApi.getById(id);
      setTrip(currentTrip);
      setError('');
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setLoading(false);
    }
  }, [id]);

  useEffect(() => {
    let active = true;
    let timeoutId;

    const poll = async () => {
      try {
        const currentTrip = await tripApi.getById(id);
        if (active) {
          setTrip(currentTrip);
          setError('');
        }
      } catch (requestError) {
        if (active) setError(getErrorMessage(requestError));
      } finally {
        if (active) {
          setLoading(false);
          timeoutId = window.setTimeout(poll, 3000);
        }
      }
    };

    poll();
    return () => {
      active = false;
      window.clearTimeout(timeoutId);
    };
  }, [id]);

  const handleConfirm = async accept => {
    setSubmitting(true);
    setError('');
    try {
      await tripApi.confirm(id, accept, accept ? paymentMethod : null);
      await refreshTrip();
    } catch (requestError) {
      setError(getErrorMessage(requestError));
    } finally {
      setSubmitting(false);
    }
  };

  if (loading && !trip) return <main><p>Đang tải chuyến...</p></main>;

  return (
    <main>
      <h1>Chi tiết chuyến</h1>
      <p>Mã chuyến: {id}</p>
      {error && <p role="alert">{error}</p>}
      {trip && (
        <>
          <p>Trạng thái: {trip.status}</p>
          <p>Điểm đón: {trip.pickup.address}</p>
          <p>Điểm đến: {trip.dropoff.address}</p>
          {trip.fare && (
            <section>
              <h2>Báo giá</h2>
              <p>Giá: {trip.fare.price}</p>
              <p>Quãng đường: {trip.fare.distanceKm} km</p>
              <p>Thời gian dự kiến: {trip.fare.durationMin} phút</p>
              <p>Chiến lược: {trip.fare.strategy}</p>
              <p>Trạng thái báo giá: {trip.fare.status}</p>
              {trip.fare.status === 'Pending' && trip.status === 'Quoted' && (
                <>
                  <label htmlFor="payment-method">Thanh toán</label>{' '}
                  <select
                    id="payment-method"
                    value={paymentMethod}
                    onChange={event => setPaymentMethod(event.target.value)}
                  >
                    <option value="WALLET">Ví</option>
                    <option value="CARD">Thẻ</option>
                  </select>
                  <div>
                    <button type="button" disabled={submitting} onClick={() => handleConfirm(true)}>
                      Đồng ý
                    </button>{' '}
                    <button type="button" disabled={submitting} onClick={() => handleConfirm(false)}>
                      Từ chối
                    </button>
                  </div>
                </>
              )}
            </section>
          )}
          {trip.driver && <p>Tài xế: {trip.driver.name}</p>}
          <section>
            <h2>Lịch sử trạng thái</h2>
            {trip.history.length === 0
              ? <p>Chưa có lịch sử.</p>
              : (
                <ol>
                  {trip.history.map((item, index) => (
                    <li key={`${item.to}-${item.at}-${index}`}>
                      {item.from ? `${item.from} → ` : ''}{item.to} ({new Date(item.at).toLocaleString()})
                    </li>
                  ))}
                </ol>
              )}
          </section>
        </>
      )}
      <p><Link to="/rider" state={{ showHome: true }}>Về trang Rider</Link></p>
    </main>
  );
}
