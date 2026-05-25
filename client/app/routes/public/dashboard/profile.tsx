import { useEffect } from "react";

export default function ProfilePage() {
	useEffect(() => {
		window.location.href = "http://iam.seliseblocks.com/profile";
	}, []);

	return null;
}
