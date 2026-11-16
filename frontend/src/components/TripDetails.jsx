import { money } from '../utils/format';

const PAYMENT_METHOD_LABEL = {
  WALLET: 'Ví',
  CARD: 'Thẻ',
};

function buildTimelineSteps(trip) {
  const historyMap = new Map();
  (trip.history || []).forEach(item => {
    historyMap.set(item.to, item.at);
  });

  const isPaymentFailed = trip.status === 'PaymentFailed';
  const isCancelled = trip.status === 'Cancelled' || trip.status === 'Failed';

  const baseSteps = [
    { key: 'Requested', label: 'Tạo yêu cầu chuyến' },
    { key: 'Quoted', label: 'Báo giá chuyến đi' },
    { key: 'DriverAccepted', label: 'Tài xế nhận chuyến' },
    { key: 'PickedUp', label: 'Đã đón khách' },
    { key: 'PaymentPending', label: 'Chờ thanh toán' },
    isPaymentFailed
      ? { key: 'PaymentFailed', label: 'Thanh toán thất bại' }
      : { key: 'Paid', label: 'Đã thanh toán' },
  ];

  if (isCancelled) {
    baseSteps.push({
      key: trip.status,
      label: trip.status === 'Cancelled' ? 'Đã hủy chuyến' : 'Không tìm được tài xế',
    });
  }

  return baseSteps.map(step => {
    const timestamp = historyMap.get(step.key);
    const completed = historyMap.has(step.key) || step.key === trip.status;
    const isCurrent = step.key === trip.status;
    return { ...step, timestamp, completed, isCurrent };
  });
}

export default function TripDetails({ trip }) {
  const timelineSteps = buildTimelineSteps(trip);
  const methodCode = trip.fare?.paymentMethod;
  const methodLabel = PAYMENT_METHOD_LABEL[methodCode] || methodCode || 'Chưa chọn';

  return <>
    <p>Trạng thái: <strong>{trip.status}</strong></p>
    <p>Điểm đón: {trip.pickup.address} ({trip.pickup.lat}, {trip.pickup.lng})</p>
    <p>Điểm đến: {trip.dropoff.address} ({trip.dropoff.lat}, {trip.dropoff.lng})</p>

    {trip.fare && <section>
      <h2>Giá chuyến</h2>
      <p>Giá: {money(trip.fare.price)}</p>
      <p>Quãng đường: {trip.fare.distanceKm} km</p>
      <p>Thời gian dự kiến: {trip.fare.durationMin} phút</p>
      <p>Thanh toán: {methodLabel}</p>
      <p>Chiến lược: {trip.fare.strategy}</p>
      <p>Trạng thái báo giá: {trip.fare.status}</p>
    </section>}

    <section>
      <h2>Tài xế</h2>
      {trip.driver ? <><p>{trip.driver.name}</p><p>Mã tài xế: {trip.driver.id}</p></>
        : <p>Chưa có tài xế nhận chuyến.</p>}
    </section>

    <section>
      <h2>Timeline hành trình & thanh toán</h2>
      <ol>
        {timelineSteps.map(step => (
          <li
            key={step.key}
            style={{
              fontWeight: step.isCurrent ? 'bold' : 'normal',
              color: step.key === 'PaymentFailed' ? 'red' : step.completed ? 'green' : '#666',
            }}
          >
            {step.completed ? '✔ ' : '○ '}
            {step.label} ({step.key})
            {step.timestamp && (
              <> <time dateTime={step.timestamp}>— {new Date(step.timestamp).toLocaleString('vi-VN')}</time></>
            )}
          </li>
        ))}
      </ol>
    </section>

    <section>
      <h2>Lịch sử trạng thái chi tiết</h2>
      {trip.history?.length ? <ol>{trip.history.map((item, index) => <li key={`${item.to}-${item.at}-${index}`}>
        {item.from ? `${item.from} → ` : ''}{item.to}{' '}
        <time dateTime={item.at}>({new Date(item.at).toLocaleString('vi-VN')})</time>
      </li>)}</ol> : <p>Chưa có lịch sử.</p>}
    </section>
  </>;
}