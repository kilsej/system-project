// ======================== Resident Page Scripts ========================

// Update display ID when Year of Residency is entered
async function updateDisplayID() {
    const yearInput = document.getElementById("YearOfResidency");
    if (!yearInput) return;

    const year = yearInput.value;

    if (year.length === 4) {
        const yy = year.substring(2); // 2025 → 25

        try {
            const response = await fetch("/Admin/GetNextResidentId");
            const nextId = await response.json(); // e.g. 1
            const formatted = nextId.toString().padStart(4, "0");
            const displayId = `R${yy}-${formatted}`;
            document.getElementById("DisplayID").value = displayId;
        } catch (error) {
            console.error("Error fetching next Resident ID:", error);
        }
    }
}


// Show popup modal with newly created credentials
document.addEventListener("DOMContentLoaded", function () {
    const username = document.getElementById("popupUsername");
    const password = document.getElementById("popupPassword");

    const tempUsername = username ? username.getAttribute("data-username") : null;
    const tempPassword = password ? password.getAttribute("data-password") : null;

    if (tempUsername && tempPassword) {
        username.value = tempUsername;
        password.value = tempPassword;

        const modal = new bootstrap.Modal(document.getElementById("successModal"));
        modal.show();
    }
});

function validateResidentForm() {
    const form = document.querySelector("#addResidentModal form");
    if (!form) return true;

    const inputs = form.querySelectorAll("input[required], input[pattern]");
    let isValid = true;

    // Clear previous validation messages
    form.querySelectorAll(".invalid-feedback").forEach(msg => msg.remove());
    inputs.forEach(i => i.classList.remove("is-invalid", "is-valid"));

    // Validation regex
    const patterns = {
        FirstName: /^[A-Za-z\s]+$/,
        LastName: /^[A-Za-z\s]+$/,
        MiddleName: /^[A-Za-z\s]*$/,
        Block: /^[0-9]+$/,
        Lot: /^[0-9]+$/,
        PhaseNo: /^[0-9]+$/,
        ContactNo: /^[0-9]{11}$/,
        Email: /^[^\s@]+@[^\s@]+\.[^\s@]+$/
    };

    inputs.forEach(input => {
        const value = input.value.trim();
        const name = input.name;
        let valid = true;
        let message = "";

        // Skip optional empty fields
        if (!value && !input.hasAttribute("required")) return;

        if (patterns[name] && !patterns[name].test(value)) {
            valid = false;

            switch (name) {
                case "FirstName":
                case "LastName":
                case "MiddleName":
                    message = "Only letters are allowed.";
                    break;
                case "Block":
                case "Lot":
                case "PhaseNo":
                    message = "Numbers only.";
                    break;
                case "ContactNo":
                    message = "Contact number must be exactly 11 digits.";
                    break;
                case "Email":
                    message = "Enter a valid email (e.g. name@example.com).";
                    break;
            }
        }

        if (!valid) {
            isValid = false;
            input.classList.add("is-invalid");
            const fb = document.createElement("div");
            fb.classList.add("invalid-feedback");
            fb.textContent = message;
            input.parentNode.appendChild(fb);
        } else {
            input.classList.add("is-valid");
        }
    });

    return isValid;
}


// Automatically clear validation styles when user types
document.addEventListener("input", function (e) {
    const el = e.target;
    if (el.classList.contains("is-valid") || el.classList.contains("is-invalid")) {
        el.classList.remove("is-valid", "is-invalid");
        const fb = el.parentNode.querySelector(".invalid-feedback");
        if (fb) fb.remove();
    }
});

function confirmAddResident() {

    // Run existing validation first
    if (!validateResidentForm()) {
        return false;
    }

    // Ask for confirmation
    return confirm("Are you sure you want to add this resident?");
}




// ======================== AUTO DATE & ADMIN NAME ========================


function loadInvoiceHeader() {
    const dateInput = document.getElementById("invDate");
    const adminInput = document.getElementById("invIssuedBy");

    if (dateInput)
        dateInput.value = new Date().toISOString().split("T")[0];

    if (adminInput) {
        const adminName = document.getElementById("adminName")?.value;
        adminInput.value = adminName;
    }
}


// ======================== UPDATE MONTH LABELS WHEN YEAR CHANGES ========================
document.addEventListener("DOMContentLoaded", function () {

    const yearSelect = document.getElementById("invYear");
    const monthLabels = document.querySelectorAll(".month-label");

    if (!yearSelect || monthLabels.length === 0) return;

    const monthNames = [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    ];

    // Load years into dropdown (2000 → currentYear, but default = currentYear)
    const currentYear = new Date().getFullYear();

    for (let y = 2000; y <= currentYear; y++) {
        let opt = document.createElement("option");
        opt.value = y;
        opt.textContent = y;

        if (y === currentYear) opt.selected = true;

        yearSelect.appendChild(opt);
    }

    function updateMonthLabels() {
        const selectedYear = yearSelect.value;

        monthLabels.forEach((label, index) => {
            label.textContent = `${monthNames[index]} – ${selectedYear}`;
        });
    }

    // Initialize labels with the current year
    updateMonthLabels();

    // Update months when dropdown changes
    yearSelect.addEventListener("change", updateMonthLabels);
});

// ======================== VALIDATE INVOICE FORM ========================
// Validate invoice form BEFORE submit
function validateInvoiceForm() {
    const block = document.getElementById("invBlock");
    const lot = document.getElementById("invLot");
    const phase = document.getElementById("invPhase");
    const name = document.getElementById("invName");
    const months = document.querySelectorAll("input[name='Months']:checked");
    const rate = document.getElementById("invRate");

    // Require block + lot to match a resident
    if (
        !block.value.trim() ||
        !lot.value.trim() ||
        !phase.value.trim() ||
        !name.value.trim()
    ) {
        alert("⚠ Please enter a valid Block & Lot with a matching resident.");
        return false;
    }


    // Months required
    if (months.length === 0) {
        alert("⚠ Please select at least ONE billing month.");
        return false;
    }

    // Rate required
    if (!rate.value || parseFloat(rate.value) <= 0) {
        alert("⚠ Please enter a valid monthly rate (greater than 0).");
        rate.focus();
        return false;
    }

    // CONFIRMATION BEFORE SUBMIT
    return confirm("Are you sure you want to submit this invoice?");
}

// ========================= ADD PAYMENT LOGIC ===============================

// Auto-fill today's date + admin name
document.addEventListener("DOMContentLoaded", function () {
    const today = new Date().toISOString().split("T")[0];

    const dateField = document.getElementById("payDate");
    if (dateField) dateField.value = today;

    const adminField = document.getElementById("payIssuedBy");
    if (adminField) adminField.value = document.getElementById("adminName")?.value || "";
});

// Listen for block/lot changes
["payBlock", "payLot"].forEach(id => {
    const el = document.getElementById(id);
    if (el) el.addEventListener("blur", loadPaymentData);
});


// ====================== VALIDATION ======================
function validatePaymentForm() {

    const block = document.getElementById("payBlock").value.trim();
    const lot = document.getElementById("payLot").value.trim();
    const phase = document.getElementById("payPhase").value.trim();
    const name = document.getElementById("payName").value.trim();

    if (!block || !lot || !phase || !name) {
        alert("⚠ Invalid Block & Lot — no matching resident found.");
        return false;
    }

    const selected = document.querySelectorAll(".pay-invoice-check:checked");
    if (selected.length === 0) {
        alert("⚠ Please select at least one invoice to pay.");
        return false;
    }

    const amount = document.getElementById("payAmount").value.trim();
    if (!amount || parseFloat(amount) <= 0) {
        alert("⚠ Invalid amount.");
        return false;
    }

    return confirm("Are you sure you want to submit this payment?");
}
