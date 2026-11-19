import { money } from '../utils/format';

const PAYMENT_METHOD_LABEL = {
  WALLET: 'Ví (WALLET)',
  CARD: 'Thẻ giả lập (CARD)',
};

const STATUS_LABELS = {
  Requested: 'Tạo yêu cầu chuyến',
  Quoted: 'Đã báo giá (chờ xác nhận)',
  DriverOffered: 'Đã gửi lời mời tài xế',
  DriverAccepted: 'Tài xế đã nhận chuyến',
  EnRoute: 'Tài xế đang đến điểm đón',
  PickedUp: 'Đã đón khách',
  DroppedOff: 'Đã trả khách',
  PaymentPending: 'Đang xử lý thanh toán',
  Paid: 'Đã thanh toán',
  PaymentFailed: 'Thanh toán thất bại',
  Cancelled: 'Đã hủy chuyến',
  Failed: 'Không tìm được tài xế',
  RefundPending: 'Đang xử lý hoàn tiền',
  Refunded: 'Đã hoàn tiền',
  RefundNeedsAttention: 'Hoàn tiền cần hỗ trợ xử lý',
};

function buildTimelineSteps(trip) {
  const historyMap = new Map();
  (trip.history || []).forEach(item => {
    historyMap.set(item.to, item.at);
  });

  const isPaymentFailed = trip.status === 'PaymentFailed' || historyMap.has('PaymentFailed');
  const isCancelled = trip.status === 'Cancelled' || trip.status === 'Failed';
  const hasRefundFlow = ['RefundPending', 'Refunded', 'RefundNeedsAttention'].some(
    s => trip.status === s || historyMap.has(s),
  );

  const baseSteps = [
    { key: 'Requested', label: STATUS_LABELS.Requested },
    { key: 'Quoted', label: STATUS_LABELS.Quoted },
  ];

  if (isCancelled) {
    baseSteps.push({
      key: trip.status,
      label: STATUS_LABELS[trip.status] || trip.status,
    });
  } else {
    baseSteps.push(
      { key: 'DriverOffered', label: STATUS_LABELS.DriverOffered },
      { key: 'DriverAccepted', label: STATUS_LABELS.DriverAccepted },
      { key: 'EnRoute', label: STATUS_LABELS.EnRoute },
      { key: 'PickedUp', label: STATUS_LABELS.PickedUp },
      { key: 'DroppedOff', label: STATUS_LABELS.DroppedOff },
      { key: 'PaymentPending', label: STATUS_LABELS.PaymentPending },
      isPaymentFailed
        ? { key: 'PaymentFailed', label: STATUS_LABELS.PaymentFailed }
        : { key: 'Paid', label: STATUS_LABELS.Paid },
    );

    if (hasRefundFlow) {
      baseSteps.push({ key: 'RefundPending', label: STATUS_LABELS.RefundPending });
      if (trip.status === 'RefundNeedsAttention' || historyMap.has('RefundNeedsAttention')) {
        baseSteps.push({ key: 'RefundNeedsAttention', label: STATUS_LABELS.RefundNeedsAttention });
      } else {
        baseSteps.push({ key: 'Refunded', label: STATUS_LABELS.Refunded });
      }
    }
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
  const statusDisplay = STATUS_LABELS[trip.status]
    ? `${trip.status} — ${STATUS_LABELS[trip.status]}`
    : trip.status;

  return <>
    <p>Trạng thái: <strong>{statusDisplay}</strong></p>
    <p>Điểm đón: {trip.pickup.address} ({trip.pickup.lat}, {trip.pickup.lng})</p>
    <p>Điểm đến: {trip.dropoff.address} ({trip.dropoff.lat}, {trip.dropoff.lng})</p>

    {trip.fare && <section>
      <h2>Giá chuyến</h2>
      <p>Giá: {money(trip.fare.price)}</p>
      <p>Quãng đường: {Number(trip.fare.distanceKm).toFixed(2)} km</p>
      <p>Thời gian dự kiến: {Number(trip.fare.durationMin).toFixed(1)} phút</p>
      <p>Thanh toán: {methodLabel}</p>
      <p>Chiến lược: {trip.fare.strategy}</p>
      <p>Trạng thái báo giá: {trip.fare.status}</p>
    </section>}

    <section>
      <h2>Tài xế</h2>
      {trip.driver ? (
        <>
          <p>Họ tên: {trip.driver.name || '(Chưa cập nhật tên)'}</p>
          <p>Mã tài xế: <code>{trip.driver.id}</code></p>
        </>
      ) : (
        <p>Chưa có tài xế nhận chuyến.</p>
      )}
    </section>

    <section>
      <h2>Timeline hành trình & thanh toán</h2>
      <ol>
        {timelineSteps.map(step => (
          <li
            key={step.key}
            style={{
              fontWeight: step.isCurrent ? 'bold' : 'normal',
            }}
          >
            {step.completed ? '[x] ' : '[ ] '}
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
