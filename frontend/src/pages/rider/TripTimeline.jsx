
const statusLabels = {
    Pending: "Đang chờ tài xế",
    Quoted: "Đã có báo giá",
    Accepted: "Tài xế đã nhận chuyến",
    Arrived: "Tài xế đã đến điểm đón",
    InProgress: "Đang di chuyển",
    Completed: "Đã hoàn thành",
    Cancelled: "Đã hủy"
};

export default function TripTimeline({ trip }) {
    // Lấy lịch sử trạng thái từ dữ liệu chuyến đi
    const history = trip.history || [];

    return (
        <div>
            <h2>Trạng thái chuyến đi</h2>

            <p>Mã chuyến: {trip.id}</p>

            <p>
                Trạng thái hiện tại:{" "}
                {statusLabels[trip.status] || trip.status}
            </p>

            <h3>Thông tin chuyến đi</h3>

            <p>Điểm đón: {trip.pickup.address}</p>
            <p>Điểm đến: {trip.dropoff.address}</p>
            <p>Giá: {trip.price}</p>

            <h3>Lịch sử chuyến đi</h3>

            {history.length === 0 ? (
                <p>Chưa có lịch sử chuyến đi.</p>
            ) : (
                <ol>
                    {history.map((item, index) => (
                        <li key={item.id || index}>
                            <p>
                                {statusLabels[item.status] || item.status}
                            </p>

                            <p>{item.description}</p>

                            <p>
                                Thời gian: {item.createdAt}
                            </p>
                        </li>
                    ))}
                </ol>
            )}
        </div>
    );
}
