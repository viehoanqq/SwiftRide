
import { useEffect, useState } from 'react';
import { useAuth } from '../../auth/AuthContext';
import { authApi } from '../../api/authApi';
import client, { getErrorMessage } from '../../api/client';

const statusLabels = {
    Accepted: "Đã nhận chuyến",
    Arrived: "Đã đến điểm đón",
    InProgress: "Đang thực hiện chuyến",
    Completed: "Đã hoàn thành",
    Cancelled: "Đã hủy"
};



export default function DriverHomePage() {
    const [requests, setRequests] = useState([]);
    const [activeTrip, setActiveTrip] = useState(null);
    const [availability, setAvailability] = useState("no");
    const { user, logout } = useAuth();
    const [me, setMe] = useState(null);
    const [error, setError] = useState('');
  
    const [loading, setLoading] = useState(true);
    const [message, setMessage] = useState("");

    // Gọi API khi tài xế thay đổi trạng thái
    async function handleAvailabilityChange(e) {
        const value = e.target.value;

        setAvailability(value);
        setMessage("");
        setError("");

        // Nếu chọn Không thì chưa gọi API,
        // vì Swagger hiện tại chưa cho thấy endpoint offline.
        if (value === "no") {
            setMessage("Bạn đã chọn không nhận chuyến.");
            return;
        }

        // Nếu chọn Có thì gọi API bật online
        if (value === "yes") {
            if (!navigator.geolocation) {
                setError("Trình duyệt không hỗ trợ lấy vị trí.");
                setAvailability("no");
                return;
            }

            setLoading(true);

            navigator.geolocation.getCurrentPosition(
                async (position) => {
                    try {
                        const lat = position.coords.latitude;
                        const lng = position.coords.longitude;

                        await client.post("/availability/online", {
                            lat: lat,
                            lng: lng
                        });

                        setMessage("Bạn đã online và sẵn sàng nhận chuyến.");
                    } catch (err) {
                        setError(getErrorMessage(err));
                        setAvailability("no");
                    } finally {
                        setLoading(false);
                    }
                },
                (err) => {
                    setError("Không thể lấy vị trí. Vui lòng cấp quyền truy cập vị trí.");
                    setAvailability("no");
                    setLoading(false);
                }
            );
        }
    }

    // Lấy danh sách lời mời chuyến và chuyến đang thực hiện
    async function loadTrips() {
        try {
            setLoading(true);
            setError("");

            const [requestResponse, activeResponse] = await Promise.all([
                client.get("/driver/trips/requests"),
                client.get("/driver/trips/active")
            ]);

            setRequests(requestResponse.data || []);
            setActiveTrip(activeResponse.data || null);

        } catch (err) {
            setError(getErrorMessage(err));
        } finally {
            setLoading(false);
        }
    }

    useEffect(() => {
        loadTrips();
    }, []);

    // UC05: Chấp nhận lời mời chuyến
    async function handleAccept(tripId) {
        try {
            setError("");
            setMessage("");

            await client.post(`/driver/trips/${tripId}/accept`);

            setMessage("Bạn đã chấp nhận chuyến đi.");
            await loadTrips();

        } catch (err) {
            setError(getErrorMessage(err));
        }
    }

    // UC05: Từ chối lời mời chuyến
    async function handleReject(tripId) {
        try {
            setError("");
            setMessage("");

            await client.post(`/driver/trips/${tripId}/reject`);

            setMessage("Bạn đã từ chối chuyến đi.");
            await loadTrips();

        } catch (err) {
            setError(getErrorMessage(err));
        }
    }

    // UC06: Cập nhật tiến trình chuyến
    async function handleUpdateStatus(newStatus) {
        try {
            setError("");
            setMessage("");

            await client.patch(
                `/driver/trips/${activeTrip.id}/status`,
                { status: newStatus }
            );

            setMessage("Cập nhật trạng thái chuyến thành công.");
            await loadTrips();

        } catch (err) {
            setError(getErrorMessage(err));
        }
    }

    if (loading) {
        return <p>Đang tải thông tin chuyến đi...</p>;
    }

    return (
        <div>
            <h1>Trang chủ tài xế SwiftRide</h1>

            <p>Chào mừng bạn đến với hệ thống tài xế SwiftRide.</p>
            <button onClick={logout}>Đăng xuất</button>
            <hr />
            {/* : sẵn sàng làm việc */}
            <div>
                <input
                    type="radio"
                    name="availability"
                    value="yes"
                    checked={availability === "yes"}
                    onChange={handleAvailabilityChange}
                    disabled={loading}
                />
                <label>Có</label>

                <input
                    type="radio"
                    name="availability"
                    value="no"
                    checked={availability === "no"}
                    onChange={handleAvailabilityChange}
                    disabled={loading}
                />
                <label>Không</label>
            </div>

            {/* UC05: Phản hồi lời mời chuyến */}
            <section>
                <h2>1. Lời mời chuyến đi</h2>

                {requests.length === 0 ? (
                    <p>Hiện tại không có lời mời chuyến đi.</p>
                ) : (
                    requests.map((trip) => (
                        <div key={trip.id}>
                            <h3>Chuyến #{trip.id}</h3>

                            <p>
                                Điểm đón: {trip.pickupLocation}
                            </p>

                            <p>
                                Điểm đến: {trip.destination}
                            </p>

                            <p>
                                Khoảng cách: {trip.distance}
                            </p>

                            <p>
                                Giá chuyến đi: {trip.price}
                            </p>

                            <p>
                                Ghi chú: {trip.note || "Không có"}
                            </p>

                            <button
                                type="button"
                                onClick={() => handleAccept(trip.id)}
                            >
                                Chấp nhận chuyến
                            </button>

                            <button
                                type="button"
                                onClick={() => handleReject(trip.id)}
                            >
                                Từ chối chuyến
                            </button>

                            <hr />
                        </div>
                    ))
                )}
            </section>

            {/* UC06: Cập nhật tiến trình chuyến */}
            <section>
                <h2>2. Chuyến đi đang thực hiện</h2>

                {!activeTrip ? (
                    <p>Bạn hiện không có chuyến đi đang thực hiện.</p>
                ) : (
                    <div>
                        <h3>Chuyến #{activeTrip.id}</h3>

                        <p>
                            Điểm đón: {activeTrip.pickupLocation}
                        </p>

                        <p>
                            Điểm đến: {activeTrip.destination}
                        </p>

                        <p>
                            Trạng thái hiện tại:{" "}
                            {statusLabels[activeTrip.status] || activeTrip.status}
                        </p>

                        <h3>Cập nhật trạng thái</h3>

                        {activeTrip.status === "Accepted" && (
                            <button
                                type="button"
                                onClick={() => handleUpdateStatus("Arrived")}
                            >
                                Đã đến điểm đón
                            </button>
                        )}

                        {activeTrip.status === "Arrived" && (
                            <button
                                type="button"
                                onClick={() => handleUpdateStatus("InProgress")}
                            >
                                Bắt đầu chuyến đi
                            </button>
                        )}

                        {activeTrip.status === "InProgress" && (
                            <button
                                type="button"
                                onClick={() => handleUpdateStatus("Completed")}
                            >
                                Hoàn thành chuyến đi
                            </button>
                        )}
                    </div>
                )}
            </section>
        </div>
    );
}
