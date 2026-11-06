import { useNavigate } from "react-router-dom";
import TripQuote from "./TripQuote";
import TripTimeline from "./TripTimeline";

export default function TripPage() {
    const navigate = useNavigate();

    // Ví dụ dữ liệu minh họa
    const trip = {
        id: 101,
        status: "Quoted",
        pickupLocation: "Đại học Sài Gòn",
        destination: "Chợ Bến Thành",
        price: 50000,
        distance: "5 km",
        estimatedDuration: "15 phút",
        strategy: "Nearest Driver",
        history: []
    };

    function handleConfirm(paymentMethod) {
        console.log("Phương thức thanh toán:", paymentMethod);

        // Sau này gọi POST /trips/{id}/confirm
    }

    function handleReject() {
        navigate("/rider/book");
    }

    if (trip.status === "Quoted") {
        return (
            <TripQuote
                trip={trip}
                onConfirm={handleConfirm}
                onReject={handleReject}
            />
        );
    }

    return <TripTimeline trip={trip} />;
}