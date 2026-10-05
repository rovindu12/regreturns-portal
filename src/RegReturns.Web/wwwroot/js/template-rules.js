// Add-rule form on the template version page: shows only the parameter group of the chosen rule type.
// Progressive enhancement: without JavaScript every group stays visible and the server reads only the chosen
// type's parameters. Hidden groups are also disabled so their inputs are not posted.
(function () {
  'use strict';

  var select = document.querySelector('[data-rule-type]');
  if (!select) {
    return;
  }

  var groups = document.querySelectorAll('[data-rule-params]');

  function showParametersOfChosenType() {
    groups.forEach(function (group) {
      var applies = group.getAttribute('data-rule-params').split(' ').indexOf(select.value) >= 0;
      group.hidden = !applies;
      group.disabled = !applies;
    });
  }

  select.addEventListener('change', showParametersOfChosenType);
  showParametersOfChosenType();
})();
